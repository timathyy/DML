using System.Collections.ObjectModel;
using Dml.Launcher.App.ViewModels;
using Dml.Launcher.Application.Downloads;
using Dml.Launcher.Application.Instances;
using Dml.Launcher.Application.Versions;
using Dml.Launcher.Domain.Downloads;
using Dml.Launcher.Domain.Instances;
using Dml.Launcher.Domain.Versions;
using Microsoft.Extensions.Logging;

namespace Dml.Launcher.App.Services;

public sealed partial class DownloadManager : IDisposable
{
    private readonly IMinecraftVersionService versionService;
    private readonly IInstanceService instanceService;
    private readonly ILogger<DownloadManager> logger;
    private readonly Dictionary<Guid, RunningDownload> downloads = [];
    private bool initialized;
    private bool disposing;

    public DownloadManager(
        IMinecraftVersionService versionService,
        IInstanceService instanceService,
        ILogger<DownloadManager> logger)
    {
        this.versionService = versionService;
        this.instanceService = instanceService;
        this.logger = logger;
    }

    public ObservableCollection<DownloadTaskViewModel> Tasks { get; } = [];

    public event EventHandler? TasksChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        IReadOnlyList<GameInstance> instances = await instanceService.GetAllAsync(cancellationToken);
        List<GameInstance> incomplete = instances
            .Where(instance => instance.InstallationState != InstanceInstallationState.Ready)
            .ToList();
        if (incomplete.Count == 0)
        {
            return;
        }

        MinecraftVersionManifest? manifest = null;
        string? manifestError = null;
        try
        {
            manifest = await versionService.GetManifestAsync(cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            manifestError = exception.Message;
            LogRestoreFailure(logger, exception);
        }

        foreach (GameInstance instance in incomplete)
        {
            if (downloads.ContainsKey(instance.Id))
            {
                continue;
            }

            MinecraftVersionInfo? version = manifest?.Versions.FirstOrDefault(item =>
                string.Equals(item.Id, instance.MinecraftVersion, StringComparison.Ordinal));
            DownloadState state = instance.InstallationState switch
            {
                InstanceInstallationState.Paused => DownloadState.Paused,
                InstanceInstallationState.Failed => DownloadState.Failed,
                _ => DownloadState.Downloading,
            };
            DownloadTaskViewModel task = new(instance.Id, instance.Name, instance.MinecraftVersion, state);
            if (state == DownloadState.Failed)
            {
                task.MarkState(DownloadState.Failed, "上次下载未完成，可点击重试继续使用已保留的分片。");
            }

            RunningDownload running = new(instance, version, task);
            downloads.Add(instance.Id, running);
            Tasks.Add(task);

            if (version is null)
            {
                string reason = manifestError is null
                    ? $"官方版本清单中找不到 {instance.MinecraftVersion}。"
                    : $"无法恢复下载：{manifestError}";
                task.MarkState(DownloadState.Failed, reason);
                await TrySetInstallationStateAsync(instance.Id, InstanceInstallationState.Failed);
            }
            else if (instance.InstallationState == InstanceInstallationState.Downloading)
            {
                StartRunning(running);
            }
        }

        NotifyTasksChanged();
    }

    public void Enqueue(GameInstance instance, MinecraftVersionInfo version)
    {
        if (downloads.ContainsKey(instance.Id))
        {
            return;
        }

        DownloadTaskViewModel task = new(instance.Id, instance.Name, version.Id, DownloadState.Preparing);
        RunningDownload running = new(instance, version, task);
        downloads.Add(instance.Id, running);
        Tasks.Add(task);
        StartRunning(running);
    }

    public async Task TogglePauseAsync(DownloadTaskViewModel task)
    {
        if (!downloads.TryGetValue(task.InstanceId, out RunningDownload? running))
        {
            return;
        }

        if (task.State is DownloadState.Paused or DownloadState.Failed)
        {
            await ResumeAsync(running);
            return;
        }

        running.PauseController?.Pause();
        task.MarkState(DownloadState.Paused);
        await TrySetInstallationStateAsync(task.InstanceId, InstanceInstallationState.Paused);
        NotifyTasksChanged();
    }

    public async Task CancelAndDeleteAsync(DownloadTaskViewModel task)
    {
        if (!downloads.TryGetValue(task.InstanceId, out RunningDownload? running))
        {
            return;
        }

        running.IsDeleting = true;
        running.Cancellation?.Cancel();
        if (running.Work is not null)
        {
            try
            {
                await running.Work;
            }
            catch (OperationCanceledException)
            {
            }
        }

        try
        {
            await instanceService.DeleteAsync(task.InstanceId);
            Remove(running);
            NotifyTasksChanged();
        }
        catch (Exception exception)
        {
            running.IsDeleting = false;
            running.Task.MarkState(DownloadState.Failed, $"无法移除未完成实例：{exception.Message}");
            await TrySetInstallationStateAsync(task.InstanceId, InstanceInstallationState.Failed);
            NotifyTasksChanged();
            throw;
        }
    }

    private async Task ResumeAsync(RunningDownload running)
    {
        if (running.Work is { IsCompleted: false })
        {
            running.PauseController!.Resume();
            running.Task.MarkState(DownloadState.Downloading);
            await TrySetInstallationStateAsync(running.Instance.Id, InstanceInstallationState.Downloading);
            NotifyTasksChanged();
            return;
        }

        if (running.Version is null)
        {
            try
            {
                MinecraftVersionManifest manifest = await versionService.GetManifestAsync(forceRefresh: true);
                running.Version = manifest.Versions.FirstOrDefault(item =>
                    string.Equals(item.Id, running.Instance.MinecraftVersion, StringComparison.Ordinal));
                if (running.Version is null)
                {
                    running.Task.MarkState(DownloadState.Failed, $"官方版本清单中找不到 {running.Instance.MinecraftVersion}。");
                    return;
                }
            }
            catch (Exception exception)
            {
                running.Task.MarkState(DownloadState.Failed, exception.Message);
                return;
            }
        }

        running.Task.MarkState(DownloadState.Preparing);
        await TrySetInstallationStateAsync(running.Instance.Id, InstanceInstallationState.Downloading);
        NotifyTasksChanged();
        StartRunning(running);
    }

    private void StartRunning(RunningDownload running)
    {
        running.Cancellation?.Dispose();
        running.Cancellation = new CancellationTokenSource();
        running.PauseController = new DownloadPauseController();
        running.Work = RunAsync(running, running.Cancellation.Token);
    }

    private async Task RunAsync(RunningDownload running, CancellationToken cancellationToken)
    {
        Progress<DownloadProgress> progress = new(value =>
        {
            if (running.PauseController?.IsPaused == true && value.State == DownloadState.Downloading)
            {
                value = value with { State = DownloadState.Paused };
            }

            running.Task.ApplyProgress(value);
        });

        try
        {
            await versionService.InstallAsync(
                running.Instance,
                running.Version!,
                running.PauseController!,
                progress,
                cancellationToken);
            if (!disposing && !running.IsDeleting)
            {
                Remove(running);
                NotifyTasksChanged();
            }
        }
        catch (OperationCanceledException)
        {
            if (!disposing && !running.IsDeleting)
            {
                running.Task.MarkState(DownloadState.Failed, "下载已中断，可点击重试继续使用已保留的分片。");
                await TrySetInstallationStateAsync(running.Instance.Id, InstanceInstallationState.Failed);
                NotifyTasksChanged();
            }
        }
        catch (Exception exception)
        {
            if (disposing || running.IsDeleting)
            {
                return;
            }

            running.Task.MarkState(DownloadState.Failed, exception.Message);
            await TrySetInstallationStateAsync(running.Instance.Id, InstanceInstallationState.Failed);
            LogDownloadFailure(logger, exception, running.Instance.Name, running.Instance.MinecraftVersion);
            NotifyTasksChanged();
        }
    }

    private async Task TrySetInstallationStateAsync(Guid instanceId, InstanceInstallationState state)
    {
        try
        {
            await instanceService.SetInstallationStateAsync(instanceId, state);
        }
        catch (Exception exception)
        {
            LogStateFailure(logger, exception, instanceId, state);
        }
    }

    private void Remove(RunningDownload running)
    {
        downloads.Remove(running.Instance.Id);
        Tasks.Remove(running.Task);
        running.Cancellation?.Dispose();
        running.Cancellation = null;
    }

    private void NotifyTasksChanged() => TasksChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        disposing = true;
        foreach (RunningDownload running in downloads.Values)
        {
            running.Cancellation?.Cancel();
        }
    }

    private sealed class RunningDownload(
        GameInstance instance,
        MinecraftVersionInfo? version,
        DownloadTaskViewModel task)
    {
        public GameInstance Instance { get; } = instance;
        public MinecraftVersionInfo? Version { get; set; } = version;
        public DownloadTaskViewModel Task { get; } = task;
        public CancellationTokenSource? Cancellation { get; set; }
        public DownloadPauseController? PauseController { get; set; }
        public Task? Work { get; set; }
        public bool IsDeleting { get; set; }
    }

    [LoggerMessage(EventId = 2200, Level = LogLevel.Warning, Message = "无法恢复未完成的下载任务。")]
    private static partial void LogRestoreFailure(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2201, Level = LogLevel.Error, Message = "实例 {instanceName} 的 Minecraft {versionId} 下载失败。")]
    private static partial void LogDownloadFailure(ILogger logger, Exception exception, string instanceName, string versionId);

    [LoggerMessage(EventId = 2202, Level = LogLevel.Warning, Message = "无法将实例 {instanceId} 的安装状态更新为 {state}。")]
    private static partial void LogStateFailure(ILogger logger, Exception exception, Guid instanceId, InstanceInstallationState state);
}
