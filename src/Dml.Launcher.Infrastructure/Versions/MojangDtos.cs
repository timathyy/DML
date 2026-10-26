using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dml.Launcher.Infrastructure.Versions;

internal sealed class ManifestDto
{
    [JsonPropertyName("latest")]
    public LatestVersionsDto Latest { get; init; } = new();

    [JsonPropertyName("versions")]
    public List<ManifestVersionDto> Versions { get; init; } = [];
}

internal sealed class LatestVersionsDto
{
    [JsonPropertyName("release")]
    public string Release { get; init; } = string.Empty;

    [JsonPropertyName("snapshot")]
    public string Snapshot { get; init; } = string.Empty;
}

internal sealed class ManifestVersionDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("sha1")]
    public string Sha1 { get; init; } = string.Empty;

    [JsonPropertyName("time")]
    public DateTimeOffset Time { get; init; }

    [JsonPropertyName("releaseTime")]
    public DateTimeOffset ReleaseTime { get; init; }
}

internal sealed class VersionMetadataDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("downloads")]
    public VersionDownloadsDto Downloads { get; init; } = new();

    [JsonPropertyName("assetIndex")]
    public DownloadArtifactDto AssetIndex { get; init; } = new();

    [JsonPropertyName("libraries")]
    public List<LibraryDto> Libraries { get; init; } = [];

    [JsonPropertyName("mainClass")]
    public string MainClass { get; init; } = string.Empty;

    [JsonPropertyName("arguments")]
    public LaunchArgumentsDto? Arguments { get; init; }

    [JsonPropertyName("minecraftArguments")]
    public string? MinecraftArguments { get; init; }

    [JsonPropertyName("assets")]
    public string? Assets { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("javaVersion")]
    public JavaVersionDto? JavaVersion { get; init; }
}

internal sealed class LaunchArgumentsDto
{
    [JsonPropertyName("game")]
    public List<JsonElement> Game { get; init; } = [];

    [JsonPropertyName("jvm")]
    public List<JsonElement> Jvm { get; init; } = [];
}

internal sealed class JavaVersionDto
{
    [JsonPropertyName("majorVersion")]
    public int MajorVersion { get; init; }
}

internal sealed class VersionDownloadsDto
{
    [JsonPropertyName("client")]
    public DownloadArtifactDto Client { get; init; } = new();
}

internal sealed class DownloadArtifactDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("sha1")]
    public string Sha1 { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;
}

internal sealed class LibraryDto
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("downloads")]
    public LibraryDownloadsDto? Downloads { get; init; }

    [JsonPropertyName("natives")]
    public Dictionary<string, string>? Natives { get; init; }

    [JsonPropertyName("rules")]
    public List<RuleDto>? Rules { get; init; }

    [JsonPropertyName("extract")]
    public ExtractRulesDto? Extract { get; init; }
}

internal sealed class ExtractRulesDto
{
    [JsonPropertyName("exclude")]
    public List<string> Exclude { get; init; } = [];
}

internal sealed class LibraryDownloadsDto
{
    [JsonPropertyName("artifact")]
    public DownloadArtifactDto? Artifact { get; init; }

    [JsonPropertyName("classifiers")]
    public Dictionary<string, DownloadArtifactDto>? Classifiers { get; init; }
}

internal sealed class RuleDto
{
    [JsonPropertyName("action")]
    public string Action { get; init; } = string.Empty;

    [JsonPropertyName("os")]
    public OperatingSystemRuleDto? OperatingSystem { get; init; }

    [JsonPropertyName("features")]
    public JsonElement? Features { get; init; }
}

internal sealed class OperatingSystemRuleDto
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("arch")]
    public string? Architecture { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }
}

internal sealed class AssetIndexDto
{
    [JsonPropertyName("objects")]
    public Dictionary<string, AssetObjectDto> Objects { get; init; } = [];
}

internal sealed class AssetObjectDto
{
    [JsonPropertyName("hash")]
    public string Hash { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }
}
