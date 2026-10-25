namespace Dml.Launcher.Infrastructure.Downloads;

internal sealed record FileDownloadRequest(Uri Url, string DestinationPath, string Sha1, long Size);
