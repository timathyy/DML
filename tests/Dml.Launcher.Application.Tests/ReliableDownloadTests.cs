using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Dml.Launcher.Application.Downloads;
using Dml.Launcher.Domain.Downloads;
using Dml.Launcher.Domain.Settings;
using Dml.Launcher.Domain.Versions;
using Dml.Launcher.Infrastructure;
using Dml.Launcher.Infrastructure.Downloads;
using Dml.Launcher.Infrastructure.Instances;
using Dml.Launcher.Infrastructure.Settings;
using Dml.Launcher.Infrastructure.Versions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dml.Launcher.Application.Tests;

#pragma warning disable CA5350 // Mojang metadata mandates SHA-1 for integrity checks.
public sealed class ReliableDownloadTests
{
    [Fact]
    public async Task PauseControllerBlocksUntilResumed()
    {
        DownloadPauseController controller = new();
        controller.Pause();
        TaskCompletionSource enteredWait = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool continued = false;
        Task waiter = Task.Run(async () =>
        {
            enteredWait.SetResult();
            await controller.WaitWhilePausedAsync(CancellationToken.None);
            continued = true;
        });

        await enteredWait.Task;
        Assert.False(continued);
        controller.Resume();
        await waiter;
        Assert.True(continued);
    }

    [Fact]
    public async Task DownloaderResumesPartialFileAndAtomicallyCompletesIt()
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            byte[] content = Enumerable.Range(0, 300_000).Select(value => (byte)(value % 251)).ToArray();
            string destination = Path.Combine(temporaryDirectory, "client.jar");
            const int ExistingLength = 81_337;
            await File.WriteAllBytesAsync(destination + ".dmlpart", content[..ExistingLength]);
            int requests = 0;
            using HttpClient client = new(new DelegateHandler((request, _) =>
            {
                requests++;
                Assert.Equal(ExistingLength, request.Headers.Range?.Ranges.Single().From);
                HttpResponseMessage response = new(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(content[ExistingLength..]),
                };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(ExistingLength, content.Length - 1, content.Length);
                return Task.FromResult(response);
            }));
            ReliableFileDownloader downloader = new(client);
            List<long> progress = [];

            await downloader.DownloadAsync(
                new FileDownloadRequest(new Uri("https://example.invalid/client.jar"), destination, Sha1(content), content.Length),
                new DownloadPauseController(),
                progress.Add,
                CancellationToken.None);

            Assert.Equal(1, requests);
            Assert.Equal(content, await File.ReadAllBytesAsync(destination, CancellationToken.None));
            Assert.False(File.Exists(destination + ".dmlpart"));
            Assert.Contains((long)ExistingLength, progress);
            Assert.Equal(content.Length, progress[^1]);
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task DownloaderReplacesSameSizeFileWhenHashIsWrong()
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            byte[] expected = Encoding.UTF8.GetBytes("verified Mojang payload");
            string destination = Path.Combine(temporaryDirectory, "library.jar");
            await File.WriteAllBytesAsync(destination, Enumerable.Repeat((byte)'x', expected.Length).ToArray());
            using HttpClient client = new(new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(expected),
            })));
            ReliableFileDownloader downloader = new(client);

            await downloader.DownloadAsync(
                new FileDownloadRequest(new Uri("https://example.invalid/library.jar"), destination, Sha1(expected), expected.Length),
                new DownloadPauseController(),
                _ => { },
                CancellationToken.None);

            Assert.Equal(expected, await File.ReadAllBytesAsync(destination, CancellationToken.None));
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task DownloaderRetriesTransientFailuresWithoutDiscardingPartialFile()
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            byte[] content = Encoding.UTF8.GetBytes("resumable content after a transient failure");
            const int ExistingLength = 12;
            string destination = Path.Combine(temporaryDirectory, "retry.jar");
            await File.WriteAllBytesAsync(destination + ".dmlpart", content[..ExistingLength]);
            int requests = 0;
            using HttpClient client = new(new DelegateHandler((request, _) =>
            {
                requests++;
                Assert.Equal(ExistingLength, request.Headers.Range?.Ranges.Single().From);
                if (requests == 1)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                }

                HttpResponseMessage response = new(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(content[ExistingLength..]),
                };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(ExistingLength, content.Length - 1, content.Length);
                return Task.FromResult(response);
            }));
            ReliableFileDownloader downloader = new(client);

            await downloader.DownloadAsync(
                new FileDownloadRequest(new Uri("https://example.invalid/retry.jar"), destination, Sha1(content), content.Length),
                new DownloadPauseController(),
                _ => { },
                CancellationToken.None);

            Assert.Equal(2, requests);
            Assert.Equal(content, await File.ReadAllBytesAsync(destination, CancellationToken.None));
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task ManifestRefreshFallsBackToVerifiedLocalCacheWhenOffline()
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            byte[] manifest = Encoding.UTF8.GetBytes("""
                {
                  "latest": { "release": "1.21.9", "snapshot": "25w43a" },
                  "versions": [
                    {
                      "id": "1.21.9",
                      "type": "release",
                      "url": "https://example.invalid/1.21.9.json",
                      "sha1": "0000000000000000000000000000000000000000",
                      "time": "2026-10-01T00:00:00Z",
                      "releaseTime": "2026-10-01T00:00:00Z"
                    }
                  ]
                }
                """);
            int requests = 0;
            using HttpClient client = new(new DelegateHandler((_, _) =>
            {
                requests++;
                return requests == 1
                    ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(manifest) })
                    : Task.FromException<HttpResponseMessage>(new HttpRequestException("offline"));
            }));
            (MojangVersionService service, _, _) = await CreateVersionServiceAsync(temporaryDirectory, client);

            MinecraftVersionManifest online = await service.GetManifestAsync(cancellationToken: CancellationToken.None);
            MinecraftVersionManifest cached = await service.GetManifestAsync(forceRefresh: true, CancellationToken.None);

            Assert.False(online.IsFromCache);
            Assert.True(cached.IsFromCache);
            Assert.Equal("1.21.9", cached.LatestRelease);
            Assert.Equal(4, requests);
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task InstallerDownloadsAndValidatesClientLibrariesAndAssets()
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            byte[] clientJar = Encoding.UTF8.GetBytes("client jar");
            byte[] libraryJar = Encoding.UTF8.GetBytes("library jar");
            byte[] asset = Encoding.UTF8.GetBytes("asset object");
            string assetHash = Sha1(asset);
            byte[] assetIndex = Encoding.UTF8.GetBytes($$"""
                { "objects": { "minecraft/sounds/test.ogg": { "hash": "{{assetHash}}", "size": {{asset.Length}} } } }
                """);
            byte[] versionMetadata = Encoding.UTF8.GetBytes($$"""
                {
                  "id": "test-version",
                  "downloads": {
                    "client": {
                      "sha1": "{{Sha1(clientJar)}}",
                      "size": {{clientJar.Length}},
                      "url": "https://example.invalid/client.jar"
                    }
                  },
                  "assetIndex": {
                    "id": "test-assets",
                    "sha1": "{{Sha1(assetIndex)}}",
                    "size": {{assetIndex.Length}},
                    "url": "https://example.invalid/index.json"
                  },
                  "libraries": [
                    {
                      "downloads": {
                        "artifact": {
                          "path": "com/example/library/1.0/library-1.0.jar",
                          "sha1": "{{Sha1(libraryJar)}}",
                          "size": {{libraryJar.Length}},
                          "url": "https://example.invalid/library.jar"
                        }
                      }
                    }
                  ]
                }
                """);
            Dictionary<string, byte[]> responses = new(StringComparer.OrdinalIgnoreCase)
            {
                ["https://example.invalid/version.json"] = versionMetadata,
                ["https://example.invalid/client.jar"] = clientJar,
                ["https://example.invalid/index.json"] = assetIndex,
                ["https://example.invalid/library.jar"] = libraryJar,
                [$"https://resources.download.minecraft.net/{assetHash[..2]}/{assetHash}"] = asset,
            };
            using HttpClient client = new(new DelegateHandler((request, _) =>
            {
                byte[] payload = responses[request.RequestUri!.AbsoluteUri];
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
            }));
            (MojangVersionService service, JsonInstanceService instances, LauncherSettings settings) =
                await CreateVersionServiceAsync(temporaryDirectory, client);
            var instance = await instances.CreateAsync("测试实例", "未安装", CancellationToken.None);
            MinecraftVersionInfo version = new(
                "test-version",
                MinecraftVersionType.Release,
                new Uri("https://example.invalid/version.json"),
                Sha1(versionMetadata),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);
            ConcurrentQueue<DownloadProgress> progress = new();

            await service.InstallAsync(
                instance,
                version,
                new DownloadPauseController(),
                new Progress<DownloadProgress>(progress.Enqueue),
                CancellationToken.None);

            Assert.Equal(clientJar, await File.ReadAllBytesAsync(Path.Combine(instance.GameDirectory, "versions", "test-version", "test-version.jar"), CancellationToken.None));
            Assert.Equal(libraryJar, await File.ReadAllBytesAsync(Path.Combine(instance.GameDirectory, "libraries", "com", "example", "library", "1.0", "library-1.0.jar"), CancellationToken.None));
            Assert.Equal(asset, await File.ReadAllBytesAsync(Path.Combine(instance.GameDirectory, "assets", "objects", assetHash[..2], assetHash), CancellationToken.None));
            Assert.Equal("test-version", Assert.Single(await instances.GetAllAsync(CancellationToken.None)).MinecraftVersion);
            Assert.DoesNotContain(Directory.EnumerateFiles(settings.GameRootDirectory, "*.dmlpart", SearchOption.AllDirectories), _ => true);
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryDirectory);
        }
    }

    private static async Task<(MojangVersionService Service, JsonInstanceService Instances, LauncherSettings Settings)> CreateVersionServiceAsync(
        string temporaryDirectory,
        HttpClient client)
    {
        AppPaths paths = new(temporaryDirectory, temporaryDirectory);
        JsonSettingsService settingsService = new(paths, NullLogger<JsonSettingsService>.Instance);
        LauncherSettings settings = new(Path.Combine(temporaryDirectory, "Game"), 3, null, LauncherTheme.System);
        await settingsService.SaveAsync(settings, CancellationToken.None);
        JsonInstanceService instances = new(paths, settingsService, NullLogger<JsonInstanceService>.Instance);
        MojangVersionService service = new(
            client,
            paths,
            instances,
            settingsService,
            NullLogger<MojangVersionService>.Instance);
        return (service, instances, settings);
    }

    private static string Sha1(byte[] content) => Convert.ToHexString(SHA1.HashData(content)).ToLowerInvariant();

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "DmlLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
#pragma warning restore CA5350
