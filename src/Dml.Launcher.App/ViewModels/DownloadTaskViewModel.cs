using CommunityToolkit.Mvvm.ComponentModel;
using Dml.Launcher.Domain.Downloads;

namespace Dml.Launcher.App.ViewModels;

public sealed partial class DownloadTaskViewModel : ObservableObject
{
    [ObservableProperty] private DownloadState state;
    [ObservableProperty] private double progressPercentage;
    [ObservableProperty] private string progressSummary = "正在准备版本元数据…";
    [ObservableProperty] private string speedSummary = string.Empty;
    [ObservableProperty] private string? errorMessage;

    public DownloadTaskViewModel(Guid instanceId, string instanceName, string versionId, DownloadState initialState)
    {
        InstanceId = instanceId;
        InstanceName = instanceName;
        VersionId = versionId;
        state = initialState;
    }

    public Guid InstanceId { get; }

    public string InstanceName { get; }

    public string VersionId { get; }

    public string StatusDisplayName => State switch
    {
        DownloadState.Preparing => "正在准备",
        DownloadState.Downloading => "正在下载",
        DownloadState.Paused => "下载暂停",
        DownloadState.Failed => "下载失败",
        _ => string.Empty,
    };

    public string PauseActionText => State switch
    {
        DownloadState.Paused => "继续",
        DownloadState.Failed => "重试",
        _ => "暂停",
    };

    public bool CanTogglePause => State is DownloadState.Preparing or DownloadState.Downloading or DownloadState.Paused or DownloadState.Failed;

    internal void ApplyProgress(DownloadProgress progress)
    {
        if (progress.State is DownloadState.Failed or DownloadState.Canceled)
        {
            if (!string.IsNullOrWhiteSpace(progress.ErrorReason))
            {
                ErrorMessage = progress.ErrorReason;
            }

            return;
        }

        State = progress.State;
        ProgressPercentage = progress.Percentage;
        ProgressSummary = progress.TotalFiles == 0
            ? "正在准备版本元数据…"
            : $"{progress.CompletedFiles}/{progress.TotalFiles} 个文件 · 剩余 {progress.RemainingFiles} 个";
        SpeedSummary = progress.BytesPerSecond <= 0
            ? $"{FormatBytes(progress.CompletedBytes)} / {FormatBytes(progress.TotalBytes)}"
            : $"{FormatBytes(progress.BytesPerSecond)}/s · {FormatBytes(progress.CompletedBytes)} / {FormatBytes(progress.TotalBytes)}";
    }

    internal void MarkState(DownloadState value, string? error = null)
    {
        State = value;
        ErrorMessage = error;
    }

    partial void OnStateChanged(DownloadState value)
    {
        OnPropertyChanged(nameof(StatusDisplayName));
        OnPropertyChanged(nameof(PauseActionText));
        OnPropertyChanged(nameof(CanTogglePause));
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
}
