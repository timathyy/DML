using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dml.Launcher.Application.Downloads;
using Dml.Launcher.Application.Instances;
using Dml.Launcher.Application.Settings;
using Dml.Launcher.Application.Versions;
using Dml.Launcher.Domain.Downloads;
using Dml.Launcher.Domain.Instances;
using Dml.Launcher.Domain.Versions;
using Dml.Launcher.Infrastructure.Downloads;
using Microsoft.Extensions.Logging;

namespace Dml.Launcher.Infrastructure.Versions;

public sealed partial class MojangVersionService : IMinecraftVersionService
{
    private static readonly Uri ManifestUri = new("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly TimeSpan ManifestCacheLifetime = TimeSpan.FromHours(6);
    private const int MaximumMetadataAttempts = 3;
    private readonly HttpClient httpClient;
    private readonly AppPaths paths;
    private readonly IInstanceService instanceService;
    private readonly ISettingsService settingsService;
    private readonly ILogger<MojangVersionService> logger;
    private readonly ReliableFileDownloader downloader;

    public MojangVersionService(
        HttpClient httpClient,
        AppPaths paths,
        IInstanceService instanceService,
        ISettingsService settingsService,
        ILogger<MojangVersionService> logger)
    {
        this.httpClient = httpClient;
        this.paths = paths;
        this.instanceService = instanceService;
        this.settingsService = settingsService;
        this.logger = logger;
        downloader = new ReliableFileDownloader(httpClient);
    }

    public async Task<MinecraftVersionManifest> GetManifestAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && File.Exists(paths.VersionManifestCachePath) &&
            DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(paths.VersionManifestCachePath) < ManifestCacheLifetime)
        {
            return await ReadManifestAsync(paths.VersionManifestCachePath, isFromCache: true, cancellationToken);
        }

        try
        {
            byte[] content = await GetBytesWithRetryAsync(ManifestUri, cancellationToken);
            MinecraftVersionManifest manifest = ParseManifest(content, DateTimeOffset.UtcNow, isFromCache: false);
            await WriteAtomicallyAsync(paths.VersionManifestCachePath, content, cancellationToken);
            LogManifestRefreshed(logger, manifest.Versions.Count);
            return manifest;
        }
        catch (Exception exception) when (exception is not OperationCanceledException && File.Exists(paths.VersionManifestCachePath))
        {
            LogManifestCacheFallback(logger, exception);
            return await ReadManifestAsync(paths.VersionManifestCachePath, isFromCache: true, cancellationToken);
        }
    }

    public async Task InstallAsync(
        GameInstance instance,
        MinecraftVersionInfo version,
        DownloadPauseController pauseController,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(pauseController);
        progress?.Report(new DownloadProgress(DownloadState.Preparing, 0, 0, 0, 0, 0, null, null));

        try
        {
            string versionDirectory = SafeCombine(instance.GameDirectory, "versions", version.Id);
            string versionJsonPath = SafeCombine(versionDirectory, version.Id + ".json");
            byte[] metadataContent = await GetVersionMetadataAsync(version, cancellationToken);
            await WriteAtomicallyAsync(versionJsonPath, metadataContent, cancellationToken);
            VersionMetadataDto metadata = JsonSerializer.Deserialize<VersionMetadataDto>(metadataContent, SerializerOptions)
                ?? throw new InvalidDataException("官方版本元数据为空。");
            ValidateVersionMetadata(metadata, version.Id);

            string assetIndexId = string.IsNullOrWhiteSpace(metadata.AssetIndex.Id) ? version.Id : metadata.AssetIndex.Id;
            string assetIndexPath = SafeCombine(instance.GameDirectory, "assets", "indexes", assetIndexId + ".json");
            FileDownloadRequest assetIndexRequest = ToRequest(metadata.AssetIndex, assetIndexPath, "资源索引");
            await downloader.DownloadAsync(assetIndexRequest, pauseController, _ => { }, cancellationToken);
            AssetIndexDto assetIndex = await ReadJsonAsync<AssetIndexDto>(assetIndexPath, cancellationToken);

            List<FileDownloadRequest> plan =
            [
                new(version.MetadataUrl, versionJsonPath, version.Sha1, metadataContent.LongLength),
                ToRequest(metadata.Downloads.Client, SafeCombine(versionDirectory, version.Id + ".jar"), "客户端"),
                assetIndexRequest,
            ];
            AddLibraries(plan, metadata.Libraries, instance.GameDirectory);
            AddAssets(plan, assetIndex, instance.GameDirectory);
            plan = plan
                .GroupBy(item => item.DestinationPath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            await DownloadPlanAsync(plan, pauseController, progress, cancellationToken);
            await instanceService.SetMinecraftVersionAsync(instance.Id, version.Id, cancellationToken);
            LogInstalled(logger, version.Id, instance.Name, plan.Count);
        }
        catch (OperationCanceledException)
        {
            progress?.Report(new DownloadProgress(DownloadState.Canceled, 0, 0, 0, 0, 0, null, "任务已取消；已下载的临时文件将在重试时继续使用。"));
            throw;
        }
        catch (Exception exception)
        {
            progress?.Report(new DownloadProgress(DownloadState.Failed, 0, 0, 0, 0, 0, null, exception.Message));
            throw;
        }
    }

    private async Task DownloadPlanAsync(
        List<FileDownloadRequest> plan,
        DownloadPauseController pauseController,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        long totalBytes = plan.Sum(item => item.Size);
        long completedBytes = 0;
        int completedFiles = 0;
        string? currentFile = null;
        Stopwatch stopwatch = Stopwatch.StartNew();
        object progressGate = new();
        long lastReportTicks = 0;

        void Report(bool force = false)
        {
            if (progress is null)
            {
                return;
            }

            lock (progressGate)
            {
                long now = stopwatch.ElapsedTicks;
                if (!force && now - lastReportTicks < Stopwatch.Frequency / 10)
                {
                    return;
                }

                lastReportTicks = now;
                double seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
                progress.Report(new DownloadProgress(
                    pauseController.IsPaused ? DownloadState.Paused : DownloadState.Downloading,
                    Volatile.Read(ref completedFiles),
                    plan.Count,
                    Volatile.Read(ref completedBytes),
                    totalBytes,
                    Volatile.Read(ref completedBytes) / seconds,
                    currentFile,
                    null));
            }
        }

        Report(force: true);
        ParallelOptions parallelOptions = new()
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = settingsService.Current.DownloadConcurrency,
        };
        await Parallel.ForEachAsync(plan, parallelOptions, async (request, taskCancellationToken) =>
        {
            currentFile = Path.GetFileName(request.DestinationPath);
            long fileBytes = 0;
            await downloader.DownloadAsync(
                request,
                pauseController,
                absoluteBytes =>
                {
                    long delta = absoluteBytes - Interlocked.Exchange(ref fileBytes, absoluteBytes);
                    if (delta > 0)
                    {
                        Interlocked.Add(ref completedBytes, delta);
                    }

                    Report();
                },
                taskCancellationToken);
            Interlocked.Increment(ref completedFiles);
            Report(force: true);
        });
        progress?.Report(new DownloadProgress(
            DownloadState.Completed,
            plan.Count,
            plan.Count,
            totalBytes,
            totalBytes,
            0,
            null,
            null));
    }

    private async Task<byte[]> GetVersionMetadataAsync(MinecraftVersionInfo version, CancellationToken cancellationToken)
    {
        string cachePath = SafeCombine(paths.MetadataCacheDirectory, "versions", version.Id + ".json");
        if (await HasSha1Async(cachePath, version.Sha1, cancellationToken))
        {
            return await File.ReadAllBytesAsync(cachePath, cancellationToken);
        }

        byte[] content = await GetBytesWithRetryAsync(version.MetadataUrl, cancellationToken);
        if (!HasSha1(content, version.Sha1))
        {
            throw new InvalidDataException($"版本 {version.Id} 的元数据 SHA-1 校验失败。");
        }

        await WriteAtomicallyAsync(cachePath, content, cancellationToken);
        return content;
    }

    private async Task<byte[]> GetBytesWithRetryAsync(Uri uri, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (int attempt = 1; attempt <= MaximumMetadataAttempts; attempt++)
        {
            try
            {
                return await httpClient.GetByteArrayAsync(uri, cancellationToken);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = exception;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            {
                lastError = exception;
            }

            if (attempt < MaximumMetadataAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }

        throw new HttpRequestException($"读取元数据失败，已重试 {MaximumMetadataAttempts} 次：{uri}", lastError);
    }

    private static void AddLibraries(List<FileDownloadRequest> plan, IEnumerable<LibraryDto> libraries, string gameDirectory)
    {
        foreach (LibraryDto library in libraries.Where(IsLibraryAllowed))
        {
            if (library.Downloads?.Artifact is { } artifact)
            {
                AddArtifact(plan, artifact, gameDirectory, "依赖库");
            }

            if (library.Natives?.TryGetValue("windows", out string? classifierName) == true &&
                library.Downloads?.Classifiers is { } classifiers)
            {
                classifierName = classifierName.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32", StringComparison.Ordinal);
                if (classifiers.TryGetValue(classifierName, out DownloadArtifactDto? classifier))
                {
                    AddArtifact(plan, classifier, gameDirectory, "原生依赖库");
                }
            }
        }
    }

    private static void AddArtifact(List<FileDownloadRequest> plan, DownloadArtifactDto artifact, string gameDirectory, string description)
    {
        if (string.IsNullOrWhiteSpace(artifact.Path))
        {
            throw new InvalidDataException($"{description}缺少目标路径。");
        }

        plan.Add(ToRequest(artifact, SafeCombine(gameDirectory, "libraries", artifact.Path), description));
    }

    private static void AddAssets(List<FileDownloadRequest> plan, AssetIndexDto index, string gameDirectory)
    {
        foreach ((string name, AssetObjectDto asset) in index.Objects)
        {
            if (asset.Hash.Length != 40 || !asset.Hash.All(Uri.IsHexDigit) || asset.Size < 0)
            {
                throw new InvalidDataException($"资源索引包含无效条目：{name}");
            }

            string prefix = asset.Hash[..2];
            Uri url = new($"https://resources.download.minecraft.net/{prefix}/{asset.Hash}");
            string destination = SafeCombine(gameDirectory, "assets", "objects", prefix, asset.Hash);
            plan.Add(new FileDownloadRequest(url, destination, asset.Hash, asset.Size));
        }
    }

    private static bool IsLibraryAllowed(LibraryDto library)
    {
        if (library.Rules is null || library.Rules.Count == 0)
        {
            return true;
        }

        bool allowed = false;
        foreach (RuleDto rule in library.Rules)
        {
            if (MatchesCurrentSystem(rule))
            {
                allowed = string.Equals(rule.Action, "allow", StringComparison.OrdinalIgnoreCase);
            }
        }

        return allowed;
    }

    private static bool MatchesCurrentSystem(RuleDto rule)
    {
        if (rule.Features is not null)
        {
            return false;
        }

        OperatingSystemRuleDto? operatingSystem = rule.OperatingSystem;
        if (operatingSystem is null)
        {
            return true;
        }

        if (operatingSystem.Name is not null && !string.Equals(operatingSystem.Name, "windows", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string architecture = RuntimeInformation.ProcessArchitecture == Architecture.X86 ? "x86" : "x64";
        if (operatingSystem.Architecture is not null &&
            !string.Equals(operatingSystem.Architecture, architecture, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return operatingSystem.Version is null ||
            Regex.IsMatch(Environment.OSVersion.VersionString, operatingSystem.Version, RegexOptions.CultureInvariant);
    }

    private static FileDownloadRequest ToRequest(DownloadArtifactDto artifact, string destination, string description)
    {
        if (!Uri.TryCreate(artifact.Url, UriKind.Absolute, out Uri? url) ||
            (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp) ||
            artifact.Size < 0 || artifact.Sha1.Length != 40 || !artifact.Sha1.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException($"{description}的下载信息无效。");
        }

        return new FileDownloadRequest(url, destination, artifact.Sha1, artifact.Size);
    }

    private static void ValidateVersionMetadata(VersionMetadataDto metadata, string expectedId)
    {
        if (!string.Equals(metadata.Id, expectedId, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(metadata.AssetIndex.Url))
        {
            throw new InvalidDataException("官方版本元数据内容无效或版本标识不匹配。");
        }
    }

    private static async Task<MinecraftVersionManifest> ReadManifestAsync(string path, bool isFromCache, CancellationToken cancellationToken)
    {
        byte[] content = await File.ReadAllBytesAsync(path, cancellationToken);
        return ParseManifest(content, File.GetLastWriteTimeUtc(path), isFromCache);
    }

    private static MinecraftVersionManifest ParseManifest(byte[] content, DateTimeOffset fetchedAt, bool isFromCache)
    {
        ManifestDto dto = JsonSerializer.Deserialize<ManifestDto>(content, SerializerOptions)
            ?? throw new InvalidDataException("Mojang 版本清单为空。");
        if (string.IsNullOrWhiteSpace(dto.Latest.Release) || dto.Versions.Count == 0)
        {
            throw new InvalidDataException("Mojang 版本清单缺少必要字段。");
        }

        List<MinecraftVersionInfo> versions = dto.Versions.Select(item =>
        {
            if (!Uri.TryCreate(item.Url, UriKind.Absolute, out Uri? url) ||
                (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp) ||
                item.Sha1.Length != 40 || !item.Sha1.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException($"版本清单中的 {item.Id} 条目无效。");
            }

            return new MinecraftVersionInfo(
                item.Id,
                ParseVersionType(item.Type),
                url,
                item.Sha1,
                item.Time,
                item.ReleaseTime);
        }).ToList();
        return new MinecraftVersionManifest(dto.Latest.Release, dto.Latest.Snapshot, versions, fetchedAt, isFromCache);
    }

    private static MinecraftVersionType ParseVersionType(string type) => type switch
    {
        "release" => MinecraftVersionType.Release,
        "snapshot" => MinecraftVersionType.Snapshot,
        "old_beta" => MinecraftVersionType.OldBeta,
        "old_alpha" => MinecraftVersionType.OldAlpha,
        _ => MinecraftVersionType.Unknown,
    };

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken)
            ?? throw new InvalidDataException($"无法解析元数据文件：{Path.GetFileName(path)}");
    }

    private static string SafeCombine(string root, params string[] segments)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(Path.Combine([root, .. segments.Select(segment => segment.Replace('/', Path.DirectorySeparatorChar))]));
        if (!candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("下载元数据包含越出实例目录的路径，已拒绝写入。");
        }

        return candidate;
    }

    private static async Task WriteAtomicallyAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = path + ".tmp";
        await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "Mojang metadata mandates SHA-1 for file integrity verification.")]
    private static bool HasSha1(byte[] content, string expectedSha1) =>
        string.Equals(Convert.ToHexString(SHA1.HashData(content)), expectedSha1, StringComparison.OrdinalIgnoreCase);

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "Mojang metadata mandates SHA-1 for file integrity verification.")]
    private static async Task<bool> HasSha1Async(string path, string expectedSha1, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA1.HashDataAsync(stream, cancellationToken);
        return string.Equals(Convert.ToHexString(hash), expectedSha1, StringComparison.OrdinalIgnoreCase);
    }

    [LoggerMessage(EventId = 200, Level = LogLevel.Information, Message = "已刷新 Mojang 版本清单，共 {count} 个版本。")]
    private static partial void LogManifestRefreshed(ILogger logger, int count);

    [LoggerMessage(EventId = 201, Level = LogLevel.Warning, Message = "无法刷新 Mojang 版本清单，已回退到本地缓存。")]
    private static partial void LogManifestCacheFallback(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 202, Level = LogLevel.Information, Message = "已为实例 {instanceName} 安装 Minecraft {versionId}，校验了 {fileCount} 个文件。")]
    private static partial void LogInstalled(ILogger logger, string versionId, string instanceName, int fileCount);
}
