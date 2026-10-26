using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dml.Launcher.Application.Instances;
using Dml.Launcher.Application.Launching;
using Dml.Launcher.Domain.Instances;
using Dml.Launcher.Domain.Launching;
using Dml.Launcher.Infrastructure.Logging;
using Dml.Launcher.Infrastructure.Versions;
using Microsoft.Extensions.Logging;

namespace Dml.Launcher.Infrastructure.Launching;

public sealed partial class MinecraftGameLauncher(
    IJavaRuntimeService javaRuntimeService,
    IInstanceService instanceService,
    ILogger<MinecraftGameLauncher> logger) : IGameLauncher
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<GameLaunchCommand> BuildCommandAsync(
        GameInstance instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (instance.InstallationState != InstanceInstallationState.Ready)
        {
            throw new InvalidOperationException("实例尚未安装完成，不能启动。");
        }

        string versionDirectory = SafeCombine(instance.GameDirectory, "versions", instance.MinecraftVersion);
        string metadataPath = SafeCombine(versionDirectory, instance.MinecraftVersion + ".json");
        if (!File.Exists(metadataPath))
        {
            throw new FileNotFoundException("找不到实例的版本元数据，请先修复或重新下载实例。", metadataPath);
        }

        await using FileStream stream = File.OpenRead(metadataPath);
        VersionMetadataDto metadata = await JsonSerializer.DeserializeAsync<VersionMetadataDto>(
            stream,
            SerializerOptions,
            cancellationToken) ?? throw new InvalidDataException("版本元数据为空。");
        if (string.IsNullOrWhiteSpace(metadata.MainClass))
        {
            throw new InvalidDataException("版本元数据缺少 mainClass，无法生成启动命令。");
        }

        JavaInstallation java = await SelectJavaAsync(instance.JavaPath, metadata.JavaVersion?.MajorVersion, cancellationToken);
        if (!java.Is64Bit && instance.MemoryMegabytes > 1536)
        {
            throw new InvalidOperationException("32 位 Java 无法使用当前内存设置，请选择 64 位 Java 或降低内存。");
        }

        string nativesDirectory = SafeCombine(instance.GameDirectory, "natives", instance.MinecraftVersion);
        Directory.CreateDirectory(nativesDirectory);
        ExtractNatives(metadata.Libraries, instance.GameDirectory, nativesDirectory);
        string classPath = BuildClassPath(metadata.Libraries, instance.GameDirectory, versionDirectory, instance.MinecraftVersion);
        string assetIndexName = metadata.AssetIndex.Id ?? metadata.Assets ?? instance.MinecraftVersion;
        Dictionary<string, string> values = CreatePlaceholderValues(instance, metadata, classPath, nativesDirectory, assetIndexName);

        List<string> arguments = [];
        arguments.Add($"-Xms{Math.Min(instance.MemoryMegabytes, 512)}M");
        arguments.Add($"-Xmx{instance.MemoryMegabytes}M");
        arguments.AddRange(ParseCommandLine(instance.JvmArguments));

        List<string> metadataJvmArguments = ExpandArguments(metadata.Arguments?.Jvm, values, instance);
        arguments.AddRange(metadataJvmArguments);
        if (!ContainsArgument(metadataJvmArguments, "-Djava.library.path="))
        {
            arguments.Add($"-Djava.library.path={nativesDirectory}");
        }

        if (!ContainsArgument(metadataJvmArguments, "-cp") && !ContainsArgument(metadataJvmArguments, "-classpath"))
        {
            arguments.Add("-cp");
            arguments.Add(classPath);
        }

        arguments.Add(metadata.MainClass);
        if (metadata.Arguments is not null)
        {
            arguments.AddRange(ExpandArguments(metadata.Arguments.Game, values, instance));
        }
        else if (!string.IsNullOrWhiteSpace(metadata.MinecraftArguments))
        {
            arguments.AddRange(ParseCommandLine(ReplacePlaceholders(metadata.MinecraftArguments, values)));
        }
        else
        {
            throw new InvalidDataException("版本元数据缺少游戏启动参数。");
        }

        if (instance.Fullscreen && !arguments.Contains("--fullscreen", StringComparer.Ordinal))
        {
            arguments.Add("--fullscreen");
        }

        return new GameLaunchCommand(java.ExecutablePath, arguments, instance.GameDirectory);
    }

    public async Task<GameLaunchResult> LaunchAsync(
        GameInstance instance,
        Action<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        GameLaunchCommand command = await BuildCommandAsync(instance, cancellationToken);
        string logDirectory = SafeCombine(instance.GameDirectory, "logs");
        Directory.CreateDirectory(logDirectory);
        string logPath = SafeCombine(logDirectory, "dml-launch-latest.log");
        await using StreamWriter launchLog = new(logPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };
        object logGate = new();
        void WriteLine(string source, string? line)
        {
            if (line is null)
            {
                return;
            }

            string redacted = LogRedactor.Redact(line);
            string formatted = $"{DateTimeOffset.Now:O} [{source}] {redacted}";
            lock (logGate)
            {
                launchLog.WriteLine(formatted);
            }

            output?.Invoke(formatted);
            LogGameOutput(logger, instance.Name, source, redacted);
        }

        ProcessStartInfo startInfo = new(command.JavaExecutablePath)
        {
            WorkingDirectory = command.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
        DateTimeOffset startedAt = DateTimeOffset.Now;
        await launchLog.WriteLineAsync($"{startedAt:O} [DML] 启动 {instance.Name}（Minecraft {instance.MinecraftVersion}）。");
        if (!process.Start())
        {
            throw new InvalidOperationException("Java 进程未能启动。");
        }

        bool forceTerminated = false;
        try
        {
            await instanceService.MarkLaunchedAsync(instance.Id, startedAt, CancellationToken.None);
            LogProcessStarted(logger, instance.Name, process.Id);
            Task readOutput = ReadLinesAsync(process.StandardOutput, line => WriteLine("stdout", line), CancellationToken.None);
            Task readError = ReadLinesAsync(process.StandardError, line => WriteLine("stderr", line), CancellationToken.None);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                forceTerminated = true;
                TryTerminate(process);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            await Task.WhenAll(readOutput, readError);
            DateTimeOffset exitedAt = DateTimeOffset.Now;
            string exitDescription = forceTerminated ? "已强制结束" : $"退出代码 {process.ExitCode}";
            await launchLog.WriteLineAsync($"{exitedAt:O} [DML] Java 进程{exitDescription}。");
            LogProcessExited(logger, instance.Name, process.ExitCode, logPath);
            if (forceTerminated)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return new GameLaunchResult(process.ExitCode, startedAt, exitedAt, logPath);
        }
        catch
        {
            TryTerminate(process);
            throw;
        }
        finally
        {
            try
            {
                await instanceService.AddPlayTimeAsync(instance.Id, DateTimeOffset.Now - startedAt, CancellationToken.None);
            }
            catch (Exception exception)
            {
                LogPlayTimeUpdateFailed(logger, instance.Name, exception);
            }
        }
    }

    private async Task<JavaInstallation> SelectJavaAsync(
        string? configuredPath,
        int? requiredMajorVersion,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            JavaInstallation configured = await javaRuntimeService.InspectAsync(configuredPath, cancellationToken);
            EnsureCompatibleVersion(configured, requiredMajorVersion);
            return configured;
        }

        IReadOnlyList<JavaInstallation> installations = await javaRuntimeService.DiscoverAsync(cancellationToken);
        JavaInstallation? selected = requiredMajorVersion is > 0
            ? installations.FirstOrDefault(item => item.MajorVersion == requiredMajorVersion)
            : installations.Count > 0 ? installations[0] : null;
        if (selected is null)
        {
            string required = requiredMajorVersion is > 0 ? $" Java {requiredMajorVersion}" : " Java";
            throw new InvalidOperationException($"没有检测到可用的{required}。请在实例启动设置中手动选择 java.exe。");
        }

        return selected;
    }

    private static void EnsureCompatibleVersion(JavaInstallation installation, int? requiredMajorVersion)
    {
        if (requiredMajorVersion is > 0 && installation.MajorVersion != requiredMajorVersion)
        {
            throw new InvalidOperationException(
                $"此 Minecraft 版本需要 Java {requiredMajorVersion}，当前选择的是 Java {installation.MajorVersion}。");
        }
    }

    private static Dictionary<string, string> CreatePlaceholderValues(
        GameInstance instance,
        VersionMetadataDto metadata,
        string classPath,
        string nativesDirectory,
        string assetIndexName) => new(StringComparer.Ordinal)
        {
            ["${auth_player_name}"] = "DML-Developer",
            ["${version_name}"] = instance.MinecraftVersion,
            ["${game_directory}"] = instance.GameDirectory,
            ["${assets_root}"] = SafeCombine(instance.GameDirectory, "assets"),
            ["${assets_index_name}"] = assetIndexName,
            ["${auth_uuid}"] = "00000000000000000000000000000000",
            ["${auth_access_token}"] = "0",
            ["${clientid}"] = string.Empty,
            ["${auth_xuid}"] = string.Empty,
            ["${user_type}"] = "legacy",
            ["${version_type}"] = metadata.Type ?? "release",
            ["${user_properties}"] = "{}",
            ["${resolution_width}"] = instance.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["${resolution_height}"] = instance.Height.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["${natives_directory}"] = nativesDirectory,
            ["${launcher_name}"] = "DML",
            ["${launcher_version}"] = "0.4",
            ["${classpath}"] = classPath,
            ["${classpath_separator}"] = Path.PathSeparator.ToString(),
            ["${library_directory}"] = SafeCombine(instance.GameDirectory, "libraries"),
        };

    private static List<string> ExpandArguments(
        IEnumerable<JsonElement>? source,
        IReadOnlyDictionary<string, string> values,
        GameInstance instance)
    {
        List<string> result = [];
        if (source is null)
        {
            return result;
        }

        foreach (JsonElement item in source)
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                result.Add(ReplacePlaceholders(item.GetString()!, values));
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("value", out JsonElement value) ||
                (item.TryGetProperty("rules", out JsonElement rules) && !AreRulesAllowed(rules, instance)))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                result.Add(ReplacePlaceholders(value.GetString()!, values));
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                result.AddRange(value.EnumerateArray()
                    .Where(element => element.ValueKind == JsonValueKind.String)
                    .Select(element => ReplacePlaceholders(element.GetString()!, values)));
            }
        }

        return result;
    }

    private static bool AreRulesAllowed(JsonElement rules, GameInstance instance)
    {
        if (rules.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        bool allowed = false;
        foreach (JsonElement rule in rules.EnumerateArray())
        {
            if (!RuleMatches(rule, instance))
            {
                continue;
            }

            allowed = rule.TryGetProperty("action", out JsonElement action) && action.GetString() == "allow";
        }

        return allowed;
    }

    private static bool RuleMatches(JsonElement rule, GameInstance instance)
    {
        if (rule.TryGetProperty("os", out JsonElement os))
        {
            if (os.TryGetProperty("name", out JsonElement name) && name.GetString() != "windows")
            {
                return false;
            }

            if (os.TryGetProperty("arch", out JsonElement architecture))
            {
                string current = RuntimeInformation.ProcessArchitecture == Architecture.X86 ? "x86" : "x64";
                if (!string.Equals(architecture.GetString(), current, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (os.TryGetProperty("version", out JsonElement version) &&
                !Regex.IsMatch(Environment.OSVersion.VersionString, version.GetString()!, RegexOptions.CultureInvariant))
            {
                return false;
            }
        }

        if (!rule.TryGetProperty("features", out JsonElement features))
        {
            return true;
        }

        foreach (JsonProperty feature in features.EnumerateObject())
        {
            bool actual = feature.Name switch
            {
                "has_custom_resolution" => !instance.Fullscreen,
                "is_demo_user" => false,
                _ => false,
            };
            if (feature.Value.ValueKind != JsonValueKind.True && feature.Value.ValueKind != JsonValueKind.False)
            {
                return false;
            }

            if (actual != feature.Value.GetBoolean())
            {
                return false;
            }
        }

        return true;
    }

    private static string BuildClassPath(
        IEnumerable<LibraryDto> libraries,
        string gameDirectory,
        string versionDirectory,
        string versionId)
    {
        List<string> paths = [];
        foreach (LibraryDto library in libraries.Where(IsLibraryAllowed))
        {
            string? relativePath = library.Downloads?.Artifact?.Path;
            if (library.Downloads is null && string.IsNullOrWhiteSpace(relativePath) && !string.IsNullOrWhiteSpace(library.Name))
            {
                relativePath = MavenPath(library.Name);
            }

            if (!string.IsNullOrWhiteSpace(relativePath))
            {
                string path = SafeCombine(gameDirectory, "libraries", relativePath);
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("实例缺少启动所需的依赖库，请修复实例。", path);
                }

                paths.Add(path);
            }
        }

        string clientJar = SafeCombine(versionDirectory, versionId + ".jar");
        if (!File.Exists(clientJar))
        {
            throw new FileNotFoundException("实例缺少 Minecraft 客户端文件，请修复实例。", clientJar);
        }

        paths.Add(clientJar);
        return string.Join(Path.PathSeparator, paths.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static void ExtractNatives(IEnumerable<LibraryDto> libraries, string gameDirectory, string destination)
    {
        foreach (LibraryDto library in libraries.Where(IsLibraryAllowed))
        {
            if (library.Natives?.TryGetValue("windows", out string? classifierName) != true ||
                string.IsNullOrWhiteSpace(classifierName) ||
                library.Downloads?.Classifiers is not { } classifiers)
            {
                continue;
            }

            classifierName = classifierName.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32", StringComparison.Ordinal);
            if (!classifiers.TryGetValue(classifierName, out DownloadArtifactDto? artifact) || string.IsNullOrWhiteSpace(artifact.Path))
            {
                continue;
            }

            string archivePath = SafeCombine(gameDirectory, "libraries", artifact.Path);
            if (!File.Exists(archivePath))
            {
                throw new FileNotFoundException("实例缺少原生依赖库，请修复实例。", archivePath);
            }

            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string normalizedName = entry.FullName.Replace('\\', '/');
                if (string.IsNullOrEmpty(entry.Name) ||
                    normalizedName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase) ||
                    library.Extract?.Exclude.Any(prefix => normalizedName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) == true)
                {
                    continue;
                }

                string outputPath = SafeCombine(destination, normalizedName);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                entry.ExtractToFile(outputPath, overwrite: true);
            }
        }
    }

    private static bool IsLibraryAllowed(LibraryDto library)
    {
        if (library.Rules is null || library.Rules.Count == 0)
        {
            return true;
        }

        bool allowed = false;
        foreach (RuleDto rule in library.Rules)
        {
            bool matches = rule.Features is null &&
                (rule.OperatingSystem?.Name is null || rule.OperatingSystem.Name == "windows") &&
                (rule.OperatingSystem?.Architecture is null ||
                    rule.OperatingSystem.Architecture == (RuntimeInformation.ProcessArchitecture == Architecture.X86 ? "x86" : "x64")) &&
                (rule.OperatingSystem?.Version is null ||
                    Regex.IsMatch(Environment.OSVersion.VersionString, rule.OperatingSystem.Version, RegexOptions.CultureInvariant));
            if (matches)
            {
                allowed = string.Equals(rule.Action, "allow", StringComparison.OrdinalIgnoreCase);
            }
        }

        return allowed;
    }

    private static string MavenPath(string name)
    {
        string[] parts = name.Split(':');
        if (parts.Length < 3)
        {
            throw new InvalidDataException($"无法解析依赖库坐标：{name}");
        }

        string fileName = $"{parts[1]}-{parts[2]}{(parts.Length > 3 ? "-" + parts[3] : string.Empty)}.jar";
        return Path.Combine(parts[0].Replace('.', Path.DirectorySeparatorChar), parts[1], parts[2], fileName);
    }

    private static bool ContainsArgument(IEnumerable<string> arguments, string prefix) =>
        arguments.Any(argument => argument.Equals(prefix, StringComparison.Ordinal) || argument.StartsWith(prefix, StringComparison.Ordinal));

    internal static IReadOnlyList<string> ParseCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return [];
        }

        List<string> result = [];
        StringBuilder current = new();
        bool quoted = false;
        int backslashes = 0;
        foreach (char character in commandLine)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                current.Append('\\', backslashes / 2);
                if (backslashes % 2 == 1)
                {
                    current.Append('"');
                }
                else
                {
                    quoted = !quoted;
                }

                backslashes = 0;
                continue;
            }

            current.Append('\\', backslashes);
            backslashes = 0;
            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(character);
            }
        }

        current.Append('\\', backslashes);
        if (quoted)
        {
            throw new ArgumentException("JVM 参数包含未闭合的引号。", nameof(commandLine));
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    private static string ReplacePlaceholders(string value, IReadOnlyDictionary<string, string> replacements)
    {
        foreach ((string placeholder, string replacement) in replacements)
        {
            value = value.Replace(placeholder, replacement, StringComparison.Ordinal);
        }

        return value;
    }

    private static async Task ReadLinesAsync(StreamReader reader, Action<string?> callback, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            callback(line);
        }
    }

    private static string SafeCombine(string root, params string[] segments)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(Path.Combine([root, .. segments.Select(segment => segment.Replace('/', Path.DirectorySeparatorChar))]));
        if (!candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("版本元数据包含越出实例目录的路径，已拒绝使用。");
        }

        return candidate;
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    [LoggerMessage(EventId = 310, Level = LogLevel.Information, Message = "实例 {instanceName} 的 Java 进程已启动，PID {processId}。")]
    private static partial void LogProcessStarted(ILogger logger, string instanceName, int processId);

    [LoggerMessage(EventId = 311, Level = LogLevel.Information, Message = "实例 {instanceName} 的 Java 进程已退出，代码 {exitCode}。启动日志：{logPath}")]
    private static partial void LogProcessExited(ILogger logger, string instanceName, int exitCode, string logPath);

    [LoggerMessage(EventId = 312, Level = LogLevel.Information, Message = "游戏输出 [{instanceName}/{source}] {line}")]
    private static partial void LogGameOutput(ILogger logger, string instanceName, string source, string line);

    [LoggerMessage(EventId = 313, Level = LogLevel.Warning, Message = "无法累计实例 {instanceName} 的游玩时长。")]
    private static partial void LogPlayTimeUpdateFailed(ILogger logger, string instanceName, Exception exception);
}
