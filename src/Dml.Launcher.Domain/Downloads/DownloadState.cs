namespace Dml.Launcher.Domain.Downloads;

public enum DownloadState
{
    Preparing,
    Downloading,
    Paused,
    Completed,
    Failed,
    Canceled,
}
