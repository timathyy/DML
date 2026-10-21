using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Dml.Launcher.App.ViewModels;
using Dml.Launcher.Application.Settings;
using Dml.Launcher.Infrastructure;
using Dml.Launcher.Infrastructure.Logging;
using Dml.Launcher.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dml.Launcher.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        serviceProvider = ConfigureServices();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        ISettingsService settingsService = serviceProvider.GetRequiredService<ISettingsService>();
        ILogger<App> logger = serviceProvider.GetRequiredService<ILogger<App>>();
        try
        {
            await settingsService.LoadAsync();
            ApplyTheme(settingsService.Current.Theme);
            LogStarted(logger);
        }
        catch (Exception exception)
        {
            LogStartupFailure(logger, exception);
            MessageBox.Show("无法加载设置，已停止启动。请查看日志页面或本地日志文件。", "DML", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        MainWindow window = serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static ServiceProvider ConfigureServices()
    {
        ServiceCollection services = new();
        AppPaths paths = new();
        InMemoryLogStore logStore = new();
        services.AddSingleton(paths);
        services.AddSingleton(logStore);
        services.AddLogging(builder => builder.ClearProviders().AddProvider(new FileLoggerProvider(paths, logStore)));
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<LogViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (serviceProvider is not null)
        {
            ILogger<App> logger = serviceProvider.GetRequiredService<ILogger<App>>();
            LogDispatcherFailure(logger, e.Exception);
        }
        MessageBox.Show("发生未处理异常，详细信息已写入脱敏日志。", "DML", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (serviceProvider is not null)
        {
            ILogger<App> logger = serviceProvider.GetRequiredService<ILogger<App>>();
            LogUnhandledFailure(logger, e.ExceptionObject as Exception, e.IsTerminating);
        }
    }

    public static void ApplyTheme(Dml.Launcher.Domain.Settings.LauncherTheme theme)
    {
        bool dark = theme == Dml.Launcher.Domain.Settings.LauncherTheme.Dark;
        Current.Resources["PageBackground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#0F172A" : "#F8FAFC"));
        Current.Resources["PageForeground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#E2E8F0" : "#0F172A"));
        Current.Resources["MutedForeground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#94A3B8" : "#64748B"));
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "DML 启动完成。")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Critical, Message = "启动时加载设置失败。")]
    private static partial void LogStartupFailure(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Critical, Message = "未处理的界面异常。")]
    private static partial void LogDispatcherFailure(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Critical, Message = "未处理的应用程序异常。终止：{isTerminating}")]
    private static partial void LogUnhandledFailure(ILogger logger, Exception? exception, bool isTerminating);
}
