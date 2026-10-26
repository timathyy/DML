using System.Diagnostics;
using System.Text.RegularExpressions;
using Dml.Launcher.Application.Launching;
using Dml.Launcher.Domain.Launching;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Dml.Launcher.Infrastructure.Launching;

public sealed partial class JavaRuntimeService(ILogger<JavaRuntimeService> logger) : IJavaRuntimeService
{
    private static readonly TimeSpan InspectionTimeout = TimeSpan.FromSeconds(8);

    public async Task<IReadOnlyList<JavaInstallation>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        List<JavaInstallation> installations = [];
        foreach (string candidate in FindCandidates().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                installations.Add(await InspectAsync(candidate, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogInvalidCandidate(logger, candidate, exception.Message);
            }
        }

        return installations
            .GroupBy(item => item.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(item => item.MajorVersion)
            .ThenBy(item => item.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<JavaInstallation> InspectAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Java 路径不能为空。", nameof(executablePath));
        }

        string path = ResolveConsoleExecutable(Path.GetFullPath(executablePath.Trim()));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("找不到 Java 可执行文件。", path);
        }

        ProcessStartInfo startInfo = new(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-XshowSettings:properties");
        startInfo.ArgumentList.Add("-version");

        using Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("无法启动 Java 版本检测进程。");
        }

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        using CancellationTokenSource timeout = new(InspectionTimeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            TryTerminate(process);
            throw new TimeoutException("Java 版本检测超时。");
        }

        string output = await standardOutput + Environment.NewLine + await standardError;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Java 版本检测失败，退出代码 {process.ExitCode}。");
        }

        return ParseInstallation(path, output);
    }

    internal static JavaInstallation ParseInstallation(string executablePath, string output)
    {
        Match versionMatch = JavaVersionRegex().Match(output);
        if (!versionMatch.Success)
        {
            versionMatch = QuotedVersionRegex().Match(output);
        }

        if (!versionMatch.Success)
        {
            throw new InvalidDataException("Java 输出中缺少版本号。");
        }

        string version = versionMatch.Groups["version"].Value;
        int majorVersion = ParseMajorVersion(version);
        Match bitnessMatch = DataModelRegex().Match(output);
        bool is64Bit = bitnessMatch.Success
            ? string.Equals(bitnessMatch.Groups["bits"].Value, "64", StringComparison.Ordinal)
            : ArchitectureRegex().Match(output).Groups["arch"].Value.Contains("64", StringComparison.OrdinalIgnoreCase);
        return new JavaInstallation(executablePath, version, majorVersion, is64Bit);
    }

    internal static int ParseMajorVersion(string version)
    {
        string[] components = version.Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        int index = components.Length > 1 && components[0] == "1" ? 1 : 0;
        if (components.Length <= index || !int.TryParse(components[index], out int major) || major <= 0)
        {
            throw new InvalidDataException($"无法识别 Java 版本：{version}");
        }

        return major;
    }

    private static IEnumerable<string> FindCandidates()
    {
        string? javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            yield return Path.Combine(javaHome, "bin", "java.exe");
        }

        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(directory.Trim('"'), "java.exe");
        }

        foreach (string registryPath in ReadRegistryJavaHomes())
        {
            yield return Path.Combine(registryPath, "bin", "java.exe");
        }

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (string vendor in new[] { "Java", "Eclipse Adoptium", "Microsoft", "Amazon Corretto", "Zulu" })
        {
            string vendorRoot = Path.Combine(programFiles, vendor);
            if (!Directory.Exists(vendorRoot))
            {
                continue;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(vendorRoot).ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string directory in directories)
            {
                yield return Path.Combine(directory, "bin", "java.exe");
            }
        }
    }

    private static IEnumerable<string> ReadRegistryJavaHomes()
    {
        string[] roots =
        [
            @"SOFTWARE\JavaSoft\Java Runtime Environment",
            @"SOFTWARE\JavaSoft\JDK",
            @"SOFTWARE\Eclipse Adoptium\JDK",
        ];
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            foreach (string root in roots)
            {
                using RegistryKey? key = baseKey.OpenSubKey(root);
                if (key is null)
                {
                    continue;
                }

                foreach (string version in key.GetSubKeyNames())
                {
                    using RegistryKey? versionKey = key.OpenSubKey(version);
                    if (versionKey?.GetValue("JavaHome") is string home && !string.IsNullOrWhiteSpace(home))
                    {
                        yield return home;
                    }
                }
            }
        }
    }

    private static string ResolveConsoleExecutable(string path)
    {
        if (string.Equals(Path.GetFileName(path), "javaw.exe", StringComparison.OrdinalIgnoreCase))
        {
            string consolePath = Path.Combine(Path.GetDirectoryName(path)!, "java.exe");
            return File.Exists(consolePath) ? consolePath : path;
        }

        return path;
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
        catch (InvalidOperationException)
        {
        }
    }

    [GeneratedRegex(@"(?im)^\s*java\.version\s*=\s*(?<version>[^\s]+)\s*$")]
    private static partial Regex JavaVersionRegex();

    [GeneratedRegex("(?im)version\\s+\"(?<version>[^\"]+)\"")]
    private static partial Regex QuotedVersionRegex();

    [GeneratedRegex(@"(?im)^\s*sun\.arch\.data\.model\s*=\s*(?<bits>32|64)\s*$")]
    private static partial Regex DataModelRegex();

    [GeneratedRegex(@"(?im)^\s*os\.arch\s*=\s*(?<arch>[^\s]+)\s*$")]
    private static partial Regex ArchitectureRegex();

    [LoggerMessage(EventId = 300, Level = LogLevel.Debug, Message = "忽略无法使用的 Java 候选 {path}：{reason}")]
    private static partial void LogInvalidCandidate(ILogger logger, string path, string reason);
}
