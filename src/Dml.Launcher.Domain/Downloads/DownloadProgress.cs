namespace Dml.Launcher.Domain.Downloads;

public sealed record DownloadProgress(
    DownloadState State,
    int CompletedFiles,
    int TotalFiles,
    long CompletedBytes,
    long TotalBytes,
    double BytesPerSecond,
    string? CurrentFile,
    string? ErrorReason)
{
    public int RemainingFiles => Math.Max(0, TotalFiles - CompletedFiles);

    public double Percentage => TotalBytes > 0
        ? Math.Clamp((double)CompletedBytes / TotalBytes * 100, 0, 100)
        : TotalFiles > 0 ? (double)CompletedFiles / TotalFiles * 100 : 0;
}
