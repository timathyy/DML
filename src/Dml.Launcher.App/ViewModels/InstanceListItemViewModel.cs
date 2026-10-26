using Dml.Launcher.Domain.Instances;

using CommunityToolkit.Mvvm.ComponentModel;

namespace Dml.Launcher.App.ViewModels;

public sealed partial class InstanceListItemViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LaunchButtonText))]
    private bool isRunning;

    public InstanceListItemViewModel(GameInstance instance, bool isRunning = false)
    {
        Instance = instance;
        this.isRunning = isRunning;
    }

    public GameInstance Instance { get; }

    public Guid Id => Instance.Id;

    public string Name => Instance.Name;

    public string MinecraftVersion => Instance.MinecraftVersion;

    public string? Loader => Instance.Loader;

    public string? JavaPath => Instance.JavaPath;

    public string GameDirectory => Instance.GameDirectory;

    public int MemoryMegabytes => Instance.MemoryMegabytes;

    public int Width => Instance.Width;

    public int Height => Instance.Height;

    public bool Fullscreen => Instance.Fullscreen;

    public string? JvmArguments => Instance.JvmArguments;

    public string WindowMode => Fullscreen ? "全屏" : $"{Width} × {Height}";

    public DateTimeOffset? LastLaunchedAt => Instance.LastLaunchedAt;

    public string TotalPlayTime
    {
        get
        {
            TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0, Instance.TotalPlaySeconds));
            return $"{(long)duration.TotalDays}d {duration.Hours}h {duration.Minutes}m";
        }
    }

    public string LaunchButtonText => IsRunning ? "强制结束" : "启动游戏";

    public bool IsReady => Instance.InstallationState == InstanceInstallationState.Ready;

    public bool HasDownloadStatus => !IsReady;

    public string DownloadStatus => Instance.InstallationState switch
    {
        InstanceInstallationState.Downloading => "正在下载",
        InstanceInstallationState.Paused => "下载暂停",
        InstanceInstallationState.Failed => "下载失败",
        _ => string.Empty,
    };
}
