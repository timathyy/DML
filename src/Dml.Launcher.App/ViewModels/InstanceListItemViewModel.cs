using Dml.Launcher.Domain.Instances;

namespace Dml.Launcher.App.ViewModels;

public sealed class InstanceListItemViewModel(GameInstance instance)
{
    public GameInstance Instance { get; } = instance;

    public Guid Id => Instance.Id;

    public string Name => Instance.Name;

    public string MinecraftVersion => Instance.MinecraftVersion;

    public string? Loader => Instance.Loader;

    public string? JavaPath => Instance.JavaPath;

    public string GameDirectory => Instance.GameDirectory;

    public int MemoryMegabytes => Instance.MemoryMegabytes;

    public DateTimeOffset? LastLaunchedAt => Instance.LastLaunchedAt;

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
