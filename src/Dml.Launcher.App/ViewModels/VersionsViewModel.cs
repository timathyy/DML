using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dml.Launcher.Application.Downloads;
using Dml.Launcher.Application.Instances;
using Dml.Launcher.Application.Versions;
using Dml.Launcher.Domain.Downloads;
using Dml.Launcher.Domain.Instances;
using Dml.Launcher.Domain.Versions;
using Microsoft.Extensions.Logging;

namespace Dml.Launcher.App.ViewModels;

public sealed partial class VersionsViewModel : ObservableObject, IDisposable
{
    private readonly IMinecraftVersionService versionService;
    private readonly IInstanceService instanceService;
    private readonly ILogger<VersionsViewModel> logger;
    private readonly List<MinecraftVersionInfo> allVersions = [];
    private CancellationTokenSource? installationCancellation;
    private DownloadPauseController? pauseController;
    private MinecraftVersionManifest? manifest;

    [ObservableProperty] private GameInstance? selectedInstance;
    [ObservableProperty] private MinecraftVersionInfo? selectedVersion;
    [ObservableProperty] private bool showSnapshots = true;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isInstalling;
    [ObservableProperty] private bool isPaused;
    [ObservableProperty] private bool canRetry;
    [ObservableProperty] private double progressPercentage;
    [ObservableProperty] private string statusMessage = "正在加载官方版本清单…";
    [ObservableProperty] private string progressSummary = "尚未开始下载";
    [ObservableProperty] private string speedSummary = string.Empty;
    [ObservableProperty] private string latestSummary = string.Empty;
    [ObservableProperty] private string? errorMessage;

    public VersionsViewModel(
        IMinecraftVersionService versionService,
        IInstanceService instanceService,
        ILogger<VersionsViewModel> logger)
    {
        this.versionService = versionService;
        this.instanceService = instanceService;
        this.logger = logger;
    }

    public ObservableCollection<MinecraftVersionInfo> Versions { get; } = [];

    public ObservableCollection<GameInstance> Instances { get; } = [];

    [RelayCommand]
    private Task LoadAsync() => LoadCoreAsync(forceRefresh: false);

    [RelayCommand]
    private Task RefreshAsync() => LoadCoreAsync(forceRefresh: true);

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        GameInstance instance = SelectedInstance!;
        MinecraftVersionInfo version = SelectedVersion!;
        installationCancellation?.Dispose();
        installationCancellation = new CancellationTokenSource();
        pauseController = new DownloadPauseController();
        IsInstalling = true;
        IsPaused = false;
        CanRetry = false;
        ErrorMessage = null;
        ProgressPercentage = 0;
        StatusMessage = $"正在为“{instance.Name}”准备 Minecraft {version.Id}…";
        NotifyCommandStates();

        Progress<DownloadProgress> progress = new(UpdateProgress);
        try
        {
            await versionService.InstallAsync(instance, version, pauseController, progress, installationCancellation.Token);
            StatusMessage = $"Minecraft {version.Id} 已完整安装并通过校验。";
            await LoadInstancesAsync(instance.Id);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "下载已取消；保留的分片可在重试时继续下载。";
            CanRetry = true;
        }
        catch (Exception exception)
        {
            LogInstallFailure(logger, exception, version.Id, instance.Name);
            ErrorMessage = exception.Message;
            StatusMessage = "安装未完成。修复网络或磁盘问题后可重试。";
            CanRetry = true;
        }
        finally
        {
            IsInstalling = false;
            IsPaused = false;
            NotifyCommandStates();
        }
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause()
    {
        pauseController!.Pause();
        IsPaused = true;
        StatusMessage = "下载已暂停。";
        NotifyCommandStates();
    }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private void Resume()
    {
        pauseController!.Resume();
        IsPaused = false;
        StatusMessage = "下载已恢复。";
        NotifyCommandStates();
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => installationCancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanRunRetry))]
    private Task RetryAsync() => InstallAsync();

    partial void OnShowSnapshotsChanged(bool value) => ApplyVersionFilter();

    partial void OnSelectedInstanceChanged(GameInstance? value) => NotifyCommandStates();

    partial void OnSelectedVersionChanged(MinecraftVersionInfo? value) => NotifyCommandStates();

    private async Task LoadCoreAsync(bool forceRefresh)
    {
        if (IsLoading || IsInstalling)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = forceRefresh ? "正在刷新 Mojang 官方版本清单…" : "正在读取版本清单…";
        try
        {
            Guid? selectedInstanceId = SelectedInstance?.Id;
            await LoadInstancesAsync(selectedInstanceId);
            manifest = await versionService.GetManifestAsync(forceRefresh);
            allVersions.Clear();
            allVersions.AddRange(manifest.Versions.Where(item =>
                item.Type is MinecraftVersionType.Release or MinecraftVersionType.Snapshot));
            ApplyVersionFilter();
            LatestSummary = $"最新正式版 {manifest.LatestRelease} · 最新快照 {manifest.LatestSnapshot}";
            StatusMessage = manifest.IsFromCache
                ? $"已加载 {Versions.Count} 个版本（使用本地缓存；可点击刷新重试联网）。"
                : $"已从 Mojang 加载 {Versions.Count} 个版本。";
        }
        catch (Exception exception)
        {
            LogManifestFailure(logger, exception);
            ErrorMessage = exception.Message;
            StatusMessage = "无法读取版本清单。请检查网络、代理设置或日志。";
        }
        finally
        {
            IsLoading = false;
            NotifyCommandStates();
        }
    }

    private async Task LoadInstancesAsync(Guid? selectedId)
    {
        IReadOnlyList<GameInstance> loaded = await instanceService.GetAllAsync();
        Instances.Clear();
        foreach (GameInstance item in loaded.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Instances.Add(item);
        }

        SelectedInstance = Instances.FirstOrDefault(item => item.Id == selectedId) ?? Instances.FirstOrDefault();
    }

    private void ApplyVersionFilter()
    {
        string? selectedId = SelectedVersion?.Id;
        Versions.Clear();
        foreach (MinecraftVersionInfo version in allVersions.Where(item =>
                     item.Type == MinecraftVersionType.Release ||
                     (ShowSnapshots && item.Type == MinecraftVersionType.Snapshot)))
        {
            Versions.Add(version);
        }

        SelectedVersion = Versions.FirstOrDefault(item => item.Id == selectedId) ?? Versions.FirstOrDefault();
        if (manifest is not null)
        {
            StatusMessage = $"当前显示 {Versions.Count} 个版本。";
        }
    }

    private void UpdateProgress(DownloadProgress progress)
    {
        if (progress.State is DownloadState.Failed or DownloadState.Canceled)
        {
            if (!string.IsNullOrWhiteSpace(progress.ErrorReason))
            {
                ErrorMessage = progress.ErrorReason;
            }

            return;
        }

        ProgressPercentage = progress.Percentage;
        ProgressSummary = progress.TotalFiles == 0
            ? "正在解析版本元数据…"
            : $"{progress.CompletedFiles}/{progress.TotalFiles} 个文件 · 剩余 {progress.RemainingFiles} 个";
        SpeedSummary = progress.BytesPerSecond <= 0
            ? FormatBytes(progress.CompletedBytes) + " / " + FormatBytes(progress.TotalBytes)
            : $"{FormatBytes(progress.BytesPerSecond)}/s · {FormatBytes(progress.CompletedBytes)} / {FormatBytes(progress.TotalBytes)}";
    }

    private bool CanInstall() => SelectedInstance is not null && SelectedVersion is not null && !IsInstalling && !IsLoading;

    private bool CanPause() => IsInstalling && !IsPaused;

    private bool CanResume() => IsInstalling && IsPaused;

    private bool CanCancel() => IsInstalling;

    private bool CanRunRetry() => CanRetry && !IsInstalling && SelectedInstance is not null && SelectedVersion is not null;

    private void NotifyCommandStates()
    {
        InstallCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
    }

    private static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        int unit = 0;
        while (bytes >= 1024 && unit < units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        return $"{bytes:0.##} {units[unit]}";
    }

    public void Dispose()
    {
        installationCancellation?.Cancel();
        installationCancellation?.Dispose();
    }

    [LoggerMessage(EventId = 2100, Level = LogLevel.Warning, Message = "读取 Mojang 版本清单失败。")]
    private static partial void LogManifestFailure(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Error, Message = "为实例 {instanceName} 安装 Minecraft {versionId} 失败。")]
    private static partial void LogInstallFailure(ILogger logger, Exception exception, string versionId, string instanceName);
}
