using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Dml.Launcher.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly SettingsViewModel settings;
    private readonly LogViewModel logs;

    [ObservableProperty]
    private ObservableObject currentPage;

    public MainViewModel(SettingsViewModel settings, LogViewModel logs)
    {
        this.settings = settings;
        this.logs = logs;
        currentPage = settings;
    }

    [RelayCommand]
    private void ShowSettings() => CurrentPage = settings;

    [RelayCommand]
    private void ShowLogs() => CurrentPage = logs;
}
