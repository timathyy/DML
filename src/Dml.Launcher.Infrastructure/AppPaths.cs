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

    public string DefaultGameDirectory => Path.Combine(RoamingDataDirectory, "Game");
}
