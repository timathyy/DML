using Dml.Launcher.App.Services;
using Dml.Launcher.Application.Downloads;
using Dml.Launcher.Application.Instances;
using Dml.Launcher.Application.Versions;
using Dml.Launcher.Domain.Downloads;
using Dml.Launcher.Domain.Instances;
using Dml.Launcher.Domain.Versions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dml.Launcher.Application.Tests;

public sealed class DownloadManagerTests
{
    [Fact]
    public async Task InitializeRestoresPersistedDownloadingInstance()
    {
        (GameInstance instance, MinecraftVersionInfo version) = CreateInstanceAndVersion();
        FakeInstanceService instances = new(instance);
        BlockingVersionService versions = new()
        {
            Manifest = new MinecraftVersionManifest(
                version.Id,
                version.Id,
                [version],
                DateTimeOffset.UtcNow,
                IsFromCache: true),
        };
        using DownloadManager manager = new(versions, instances, NullLogger<DownloadManager>.Instance);

        await manager.InitializeAsync();
        await versions.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var task = Assert.Single(manager.Tasks);
        Assert.Equal(instance.Id, task.InstanceId);
        Assert.Equal(version.Id, task.VersionId);
    }

    [Fact]
    public async Task QueuePersistsPauseAndResumeAndCancelDeletesIncompleteInstance()
    {
        (GameInstance instance, MinecraftVersionInfo version) = CreateInstanceAndVersion();
        FakeInstanceService instances = new(instance);
        BlockingVersionService versions = new();
        using DownloadManager manager = new(versions, instances, NullLogger<DownloadManager>.Instance);

        manager.Enqueue(instance, version);
        await versions.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var task = Assert.Single(manager.Tasks);

        await manager.TogglePauseAsync(task);
        Assert.Equal(DownloadState.Paused, task.State);
        Assert.Equal(InstanceInstallationState.Paused, instances.Current!.InstallationState);

        await manager.TogglePauseAsync(task);
        Assert.Equal(DownloadState.Downloading, task.State);
        Assert.Equal(InstanceInstallationState.Downloading, instances.Current!.InstallationState);

        await manager.CancelAndDeleteAsync(task);
        Assert.Empty(manager.Tasks);
        Assert.Null(instances.Current);
    }

    private static (GameInstance Instance, MinecraftVersionInfo Version) CreateInstanceAndVersion()
    {
        GameInstance instance = new(
            Guid.NewGuid(),
            "测试实例",
            "test-version",
            null,
            null,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            2048,
            1280,
            720,
            null,
            InstanceInstallationState.Downloading);
        MinecraftVersionInfo version = new(
            "test-version",
            MinecraftVersionType.Release,
            new Uri("https://example.invalid/version.json"),
            new string('0', 40),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        return (instance, version);
    }

    private sealed class BlockingVersionService : IMinecraftVersionService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MinecraftVersionManifest? Manifest { get; init; }

        public Task<MinecraftVersionManifest> GetManifestAsync(bool forceRefresh = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(Manifest ?? throw new NotSupportedException());

        public async Task InstallAsync(
            GameInstance instance,
            MinecraftVersionInfo version,
            DownloadPauseController pauseController,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class FakeInstanceService(GameInstance instance) : IInstanceService
    {
        public GameInstance? Current { get; private set; } = instance;

        public Task<IReadOnlyList<GameInstance>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GameInstance>>(Current is null ? [] : [Current]);

        public Task<GameInstance> CreateAsync(string name, string minecraftVersion, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GameInstance> CopyAsync(Guid sourceId, string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RenameAsync(Guid id, string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetMinecraftVersionAsync(Guid id, string minecraftVersion, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetInstallationStateAsync(
            Guid id,
            InstanceInstallationState installationState,
            CancellationToken cancellationToken = default)
        {
            Current = Current! with { InstallationState = installationState };
            return Task.CompletedTask;
        }

        public Task UpdateLaunchSettingsAsync(
            Guid id,
            string? javaPath,
            int memoryMegabytes,
            int width,
            int height,
            bool fullscreen,
            string? jvmArguments,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task MarkLaunchedAsync(Guid id, DateTimeOffset launchedAt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Current = null;
            return Task.CompletedTask;
        }

        public Task<GameInstance> ImportAsync(string sourceDirectory, string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
