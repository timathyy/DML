using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Dml.Launcher.Application.Downloads;

namespace Dml.Launcher.Infrastructure.Downloads;

internal sealed class ReliableFileDownloader
{
    private const int BufferSize = 128 * 1024;
    private const int MaximumAttempts = 3;
    private readonly HttpClient httpClient;

    public ReliableFileDownloader(HttpClient httpClient) => this.httpClient = httpClient;

    public async Task DownloadAsync(
        FileDownloadRequest request,
        DownloadPauseController pauseController,
        Action<long> reportAbsoluteBytes,
        CancellationToken cancellationToken)
    {
        if (await IsValidFileAsync(request.DestinationPath, request.Size, request.Sha1, cancellationToken))
        {
            reportAbsoluteBytes(request.Size);
            return;
        }

        DeleteIfExists(request.DestinationPath);
        string temporaryPath = request.DestinationPath + ".dmlpart";
        Directory.CreateDirectory(Path.GetDirectoryName(request.DestinationPath)!);

        Exception? lastError = null;
        for (int attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                await DownloadAttemptAsync(request, temporaryPath, pauseController, reportAbsoluteBytes, cancellationToken);
                if (!await IsValidFileAsync(temporaryPath, request.Size, request.Sha1, cancellationToken))
                {
                    DeleteIfExists(temporaryPath);
                    throw new InvalidDataException($"下载文件校验失败：{Path.GetFileName(request.DestinationPath)}");
                }

                File.Move(temporaryPath, request.DestinationPath, overwrite: true);
                reportAbsoluteBytes(request.Size);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException)
            {
                lastError = exception;
                if (attempt == MaximumAttempts)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }

        throw new IOException($"下载失败，已重试 {MaximumAttempts} 次：{request.Url}", lastError);
    }

    private async Task DownloadAttemptAsync(
        FileDownloadRequest request,
        string temporaryPath,
        DownloadPauseController pauseController,
        Action<long> reportAbsoluteBytes,
        CancellationToken cancellationToken)
    {
        long existingLength = File.Exists(temporaryPath) ? new FileInfo(temporaryPath).Length : 0;
        if (existingLength > request.Size)
        {
            DeleteIfExists(temporaryPath);
            existingLength = 0;
        }

        if (existingLength == request.Size && existingLength > 0)
        {
            reportAbsoluteBytes(existingLength);
            return;
        }

        using HttpRequestMessage message = new(HttpMethod.Get, request.Url);
        if (existingLength > 0)
        {
            message.Headers.Range = new RangeHeaderValue(existingLength, null);
        }

        using HttpResponseMessage response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        bool canAppend = existingLength > 0 &&
            response.StatusCode == HttpStatusCode.PartialContent &&
            response.Content.Headers.ContentRange?.From == existingLength;
        if (existingLength > 0 && !canAppend)
        {
            existingLength = 0;
            DeleteIfExists(temporaryPath);
        }

        reportAbsoluteBytes(existingLength);

        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream output = new(
            temporaryPath,
            canAppend ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        byte[] buffer = new byte[BufferSize];
        long written = existingLength;
        while (true)
        {
            await pauseController.WaitWhilePausedAsync(cancellationToken);
            int count = await input.ReadAsync(buffer, cancellationToken);
            if (count == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            written += count;
            if (written > request.Size)
            {
                throw new InvalidDataException($"服务器返回的数据大于清单声明的大小：{request.Url}");
            }

            reportAbsoluteBytes(written);
        }

        await output.FlushAsync(cancellationToken);
    }

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "Mojang metadata mandates SHA-1 for file integrity verification.")]
    internal static async Task<bool> IsValidFileAsync(
        string path,
        long expectedSize,
        string expectedSha1,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != expectedSize)
        {
            return false;
        }

        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA1.HashDataAsync(stream, cancellationToken);
        return string.Equals(Convert.ToHexString(hash), expectedSha1, StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
