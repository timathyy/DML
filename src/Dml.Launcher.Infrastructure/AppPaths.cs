namespace Dml.Launcher.Infrastructure;

public sealed class AppPaths
{
    public AppPaths(string? roamingAppData = null, string? localAppData = null)
    {
        RoamingDataDirectory = Path.Combine(
            roamingAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DML");
        LocalDataDirectory = Path.Combine(
            localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DML");
    }

    public string RoamingDataDirectory { get; }

    public string LocalDataDirectory { get; }

    public string SettingsFilePath => Path.Combine(RoamingDataDirectory, "settings.json");

    public string InstancesFilePath => Path.Combine(RoamingDataDirectory, "instances.json");

    public string LogDirectory => Path.Combine(LocalDataDirectory, "Logs");

    public string MetadataCacheDirectory => Path.Combine(LocalDataDirectory, "Cache", "Metadata");

    public string VersionManifestCachePath => Path.Combine(MetadataCacheDirectory, "version_manifest_v2.json");

    public string DefaultGameDirectory => Path.Combine(RoamingDataDirectory, "Game");
}
