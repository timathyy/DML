namespace Dml.Launcher.Domain.Versions;

public sealed record MinecraftVersionManifest(
    string LatestRelease,
    string LatestSnapshot,
    IReadOnlyList<MinecraftVersionInfo> Versions,
    DateTimeOffset FetchedAt,
    bool IsFromCache);
