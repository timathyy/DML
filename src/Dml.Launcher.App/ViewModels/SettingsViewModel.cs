using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using Dml.Launcher.Application.Settings;
using Dml.Launcher.Domain.Settings;
using Microsoft.Extensions.Logging;

namespace Dml.Launcher.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService settingsService;
    private readonly ILogger<SettingsViewModel> logger;

    [ObservableProperty]
    private string gameRootDirectory;

    [ObservableProperty]
    private int downloadConcurrency;

    [ObservableProperty]
    private string? proxyAddress;

    [ObservableProperty]
    private LauncherTheme selectedTheme;

    [ObservableProperty]
    private string? statusMessage;

    public SettingsViewModel(ISettingsService settingsService, ILogger<SettingsViewModel> logger)
    {
        this.settingsService = settingsService;
        this.logger = logger;
        LauncherSettings settings = settingsService.Current;
        gameRootDirectory = settings.GameRootDirectory;
        downloadConcurrency = settings.DownloadConcurrency;
        proxyAddress = settings.ProxyAddress;
        selectedTheme = settings.Theme;
    }

    public Array Themes { get; } = Enum.GetValues<LauncherTheme>();

    public IReadOnlyList<int> DownloadConcurrencyChoices { get; } = [1, 2, 3, 4, 5, 6, 8, 12, 16];

    partial void OnSelectedThemeChanged(LauncherTheme value) => App.ApplyTheme(value);

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            LauncherSettings settings = new(GameRootDirectory, DownloadConcurrency, ProxyAddress, SelectedTheme);
            await settingsService.SaveAsync(settings);
            StatusMessage = "设置已保存。";
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            LogSaveFailure(logger, exception);
            StatusMessage = $"保存失败：{exception.Message}";
        }
    }

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning, Message = "保存设置失败。")]
    private static partial void LogSaveFailure(ILogger logger, Exception exception);
}
