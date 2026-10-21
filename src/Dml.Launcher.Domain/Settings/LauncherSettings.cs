namespace Dml.Launcher.Domain.Settings;

public sealed record LauncherSettings(
    string GameRootDirectory,
    int DownloadConcurrency,
    string? ProxyAddress,
    LauncherTheme Theme)
{
    public const int MinimumDownloadConcurrency = 1;
    public const int MaximumDownloadConcurrency = 16;
}
