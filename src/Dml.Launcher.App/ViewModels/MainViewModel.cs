using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Dml.Launcher.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly SettingsViewModel settings;
    private readonly LogViewModel logs;
    private readonly InstancesViewModel instances;
    private readonly VersionsViewModel versions;

    [ObservableProperty]
    private ObservableObject currentPage;

    [ObservableProperty]
    private bool isSettingsActive;

    [ObservableProperty]
    private bool isInstancesActive = true;

    [ObservableProperty]
    private bool isVersionsActive;

    [ObservableProperty]
    private bool isLogsActive;

    public MainViewModel(SettingsViewModel settings, LogViewModel logs, InstancesViewModel instances, VersionsViewModel versions)
    {
        this.settings = settings;
        this.logs = logs;
        this.instances = instances;
        this.versions = versions;
        currentPage = instances;
    }

    [RelayCommand]
    private void ShowInstances()
    {
        CurrentPage = instances;
        IsInstancesActive = true;
        IsVersionsActive = false;
        IsSettingsActive = false;
        IsLogsActive = false;
    }

    [RelayCommand]
    private void ShowVersions()
    {
        CurrentPage = versions;
        IsInstancesActive = false;
        IsVersionsActive = true;
        IsSettingsActive = false;
        IsLogsActive = false;
    }

    [RelayCommand]
    private void ShowSettings()
    {
        CurrentPage = settings;
        IsInstancesActive = false;
        IsVersionsActive = false;
        IsSettingsActive = true;
        IsLogsActive = false;
    }

    [RelayCommand]
    private void ShowLogs()
    {
        CurrentPage = logs;
        IsInstancesActive = false;
        IsVersionsActive = false;
        IsSettingsActive = false;
        IsLogsActive = true;
    }
}
