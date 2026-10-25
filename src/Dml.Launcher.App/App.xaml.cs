using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Net;
using System.Net.Http;
using Microsoft.Win32;
using Dml.Launcher.App.ViewModels;
using Dml.Launcher.Application.Settings;
using Dml.Launcher.Application.Instances;
using Dml.Launcher.Application.Versions;
using Dml.Launcher.Domain.Settings;
using Dml.Launcher.Infrastructure;
using Dml.Launcher.Infrastructure.Instances;
using Dml.Launcher.Infrastructure.Logging;
using Dml.Launcher.Infrastructure.Settings;
using Dml.Launcher.Infrastructure.Versions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dml.Launcher.App;

public partial class App : System.Windows.Application
{
    private static Dml.Launcher.Domain.Settings.LauncherTheme activeTheme = Dml.Launcher.Domain.Settings.LauncherTheme.System;
    private ServiceProvider? serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        serviceProvider = ConfigureServices();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

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
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
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
        services.AddSingleton<IInstanceService, JsonInstanceService>();
        services.AddSingleton(serviceProvider =>
        {
            LauncherSettings settings = serviceProvider.GetRequiredService<ISettingsService>().Current;
            SocketsHttpHandler handler = new()
            {
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(20),
            };
            if (settings.ProxyAddress is not null)
            {
                handler.Proxy = new WebProxy(settings.ProxyAddress);
                handler.UseProxy = true;
            }

            HttpClient client = new(handler) { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DML/0.3");
            return client;
        });
        services.AddSingleton<IMinecraftVersionService, MojangVersionService>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<LogViewModel>();
        services.AddSingleton<InstancesViewModel>();
        services.AddSingleton<VersionsViewModel>();
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
        activeTheme = theme;
        bool dark = theme == Dml.Launcher.Domain.Settings.LauncherTheme.Dark ||
                    (theme == Dml.Launcher.Domain.Settings.LauncherTheme.System && IsSystemDarkTheme());
        Current.Resources["AppBackgroundBrush"] = CreateBrush(dark ? "#212121" : "#F7F7F8");
        Current.Resources["SurfaceBrush"] = CreateBrush(dark ? "#2A2A2A" : "#FFFFFF");
        Current.Resources["SurfaceHoverBrush"] = CreateBrush(dark ? "#353535" : "#F1F1F3");
        Current.Resources["InputBrush"] = CreateBrush(dark ? "#303030" : "#FFFFFF");
        Current.Resources["PageForeground"] = CreateBrush(dark ? "#F5F5F5" : "#202123");
        Current.Resources["MutedForeground"] = CreateBrush(dark ? "#B4B4B4" : "#6F7076");
        Current.Resources["SubtleForegroundBrush"] = CreateBrush(dark ? "#858585" : "#96979E");
        Current.Resources["BorderBrush"] = CreateBrush(dark ? "#424242" : "#E7E7E9");
        Current.Resources["AccentBrush"] = CreateBrush(dark ? "#19B887" : "#10A37F");
        Current.Resources["AccentHoverBrush"] = CreateBrush(dark ? "#31C99A" : "#0E9272");
        Current.Resources["AccentForegroundBrush"] = CreateBrush("#FFFFFF");
        Current.Resources["NavigationSelectedBrush"] = CreateBrush(dark ? "#3A3A3A" : "#ECECEE");
        Current.Resources["LogBackgroundBrush"] = CreateBrush(dark ? "#171717" : "#F0F0F2");
        Current.Resources["LogForegroundBrush"] = CreateBrush(dark ? "#D7D8DC" : "#303136");
    }

    private static SolidColorBrush CreateBrush(string color) => new((Color)ColorConverter.ConvertFromString(color));

    private static bool IsSystemDarkTheme()
    {
        const string PersonalizeKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize";
        const string AppsUseLightTheme = "AppsUseLightTheme";
        object? setting = Registry.CurrentUser.OpenSubKey(PersonalizeKey)?.GetValue(AppsUseLightTheme);
        return setting is int value && value == 0;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (activeTheme == Dml.Launcher.Domain.Settings.LauncherTheme.System)
        {
            Dispatcher.Invoke(() => ApplyTheme(activeTheme));
        }
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
