using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Dml.Launcher.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly SettingsViewModel settings;
    private readonly LogViewModel logs;
    private readonly InstancesViewModel instances;

    [ObservableProperty]
    private ObservableObject currentPage;

    [ObservableProperty]
    private bool isSettingsActive;

    [ObservableProperty]
    private bool isInstancesActive = true;

    [ObservableProperty]
    private bool isLogsActive;

    public MainViewModel(SettingsViewModel settings, LogViewModel logs, InstancesViewModel instances)
    {
        this.settings = settings;
        this.logs = logs;
        this.instances = instances;
        currentPage = instances;
    }

    [RelayCommand]
    private void ShowInstances()
    {
        CurrentPage = instances;
        IsInstancesActive = true;
        IsSettingsActive = false;
        IsLogsActive = false;
    }

    [RelayCommand]
    private void ShowSettings()
    {
        CurrentPage = settings;
        IsInstancesActive = false;
        IsSettingsActive = true;
        IsLogsActive = false;
    }

    [RelayCommand]
    private void ShowLogs()
    {
        CurrentPage = logs;
        IsInstancesActive = false;
        IsSettingsActive = false;
        IsLogsActive = true;
    }
}
