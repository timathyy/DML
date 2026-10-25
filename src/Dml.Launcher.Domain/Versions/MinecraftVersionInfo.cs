namespace Dml.Launcher.Domain.Versions;

public sealed record MinecraftVersionInfo(
    string Id,
    MinecraftVersionType Type,
    Uri MetadataUrl,
    string Sha1,
    DateTimeOffset PublishedAt,
    DateTimeOffset ReleasedAt)
{
    public string TypeDisplayName => Type switch
    {
        MinecraftVersionType.Release => "正式版",
        MinecraftVersionType.Snapshot => "快照版",
        MinecraftVersionType.OldBeta => "早期测试版",
        MinecraftVersionType.OldAlpha => "早期开发版",
        _ => "未知",
    };
}
