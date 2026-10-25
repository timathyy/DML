using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Dml.Launcher.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly SettingsViewModel settings;
    private readonly LogViewModel logs;
    private readonly InstancesViewModel instances;
    private readonly DownloadsViewModel downloads;

    [ObservableProperty]
    private ObservableObject currentPage;

    [ObservableProperty]
    private bool isSettingsActive;

    [ObservableProperty]
    private bool isInstancesActive = true;

    [ObservableProperty]
    private bool isDownloadsActive;

    [ObservableProperty]
    private bool isLogsActive;

    public MainViewModel(SettingsViewModel settings, LogViewModel logs, InstancesViewModel instances, DownloadsViewModel downloads)
    {
        this.settings = settings;
        this.logs = logs;
        this.instances = instances;
        this.downloads = downloads;
        currentPage = instances;
    }

    [RelayCommand]
    private void ShowInstances()
    {
        CurrentPage = instances;
        IsInstancesActive = true;
        IsDownloadsActive = false;
        IsSettingsActive = false;
        IsLogsActive = false;
    }

    [RelayCommand]
    private void ShowDownloads()
    {
        CurrentPage = downloads;
        IsInstancesActive = false;
        IsDownloadsActive = true;
        IsSettingsActive = false;
        IsLogsActive = false;
    }

    [RelayCommand]
    private void ShowSettings()
    {
        CurrentPage = settings;
        IsInstancesActive = false;
        IsDownloadsActive = false;
        IsSettingsActive = true;
        IsLogsActive = false;
    }

    [RelayCommand]
    private void ShowLogs()
    {
        CurrentPage = logs;
        IsInstancesActive = false;
        IsDownloadsActive = false;
        IsSettingsActive = false;
        IsLogsActive = true;
    }
}
