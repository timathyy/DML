using System.Text.Json;
using Dml.Launcher.Application.Settings;
using Dml.Launcher.Domain.Settings;
using Microsoft.Extensions.Logging;

namespace Dml.Launcher.Infrastructure.Settings;

public sealed partial class JsonSettingsService(AppPaths paths, ILogger<JsonSettingsService> logger) : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public LauncherSettings Current { get; private set; } = CreateDefault(paths);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(paths.SettingsFilePath))
        {
            Current = CreateDefault(paths);
            return;
        }

        try
        {
            await using FileStream stream = File.OpenRead(paths.SettingsFilePath);
            LauncherSettings? settings = await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, JsonOptions, cancellationToken);
            Current = Validate(settings ?? CreateDefault(paths));
        }
        catch (JsonException exception)
        {
            LogInvalidFormat(logger, exception);
            Current = CreateDefault(paths);
        }
        catch (ArgumentException exception)
        {
            LogInvalidContent(logger, exception);
            Current = CreateDefault(paths);
        }
        catch (IOException exception)
        {
            LogReadFailed(logger, exception);
            Current = CreateDefault(paths);
        }
    }

    public async Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        LauncherSettings validated = Validate(settings);
        Directory.CreateDirectory(paths.RoamingDataDirectory);
        string temporaryPath = paths.SettingsFilePath + ".tmp";

        await using (FileStream stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, validated, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, paths.SettingsFilePath, true);
        Current = validated;
        LogSaved(logger, validated.DownloadConcurrency, validated.Theme);
    }

    private static LauncherSettings CreateDefault(AppPaths paths) => new(
        paths.DefaultGameDirectory,
        4,
        null,
        LauncherTheme.System);

    private static LauncherSettings Validate(LauncherSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.GameRootDirectory))
        {
            throw new ArgumentException("游戏根目录不能为空。", nameof(settings));
        }

        if (settings.DownloadConcurrency is < LauncherSettings.MinimumDownloadConcurrency or > LauncherSettings.MaximumDownloadConcurrency)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "下载并发数必须介于 1 到 16 之间。");
        }

        if (!string.IsNullOrWhiteSpace(settings.ProxyAddress) && !Uri.TryCreate(settings.ProxyAddress, UriKind.Absolute, out _))
        {
            throw new ArgumentException("代理地址必须是完整的 URI。", nameof(settings));
        }

        return settings with
        {
            GameRootDirectory = Path.GetFullPath(settings.GameRootDirectory.Trim()),
            ProxyAddress = string.IsNullOrWhiteSpace(settings.ProxyAddress) ? null : settings.ProxyAddress.Trim(),
        };
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "设置文件格式无效，已使用默认设置。")]
    private static partial void LogInvalidFormat(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "设置文件内容无效，已使用默认设置。")]
    private static partial void LogInvalidContent(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "无法读取设置文件，已使用默认设置。")]
    private static partial void LogReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "设置已保存：下载并发数 {downloadConcurrency}，主题 {theme}。")]
    private static partial void LogSaved(ILogger logger, int downloadConcurrency, LauncherTheme theme);
}
