using System.IO.Compression;
using System.Text;
using Dml.Launcher.Application.Instances;
using Dml.Launcher.Application.Launching;
using Dml.Launcher.Domain.Instances;
using Dml.Launcher.Domain.Launching;
using Dml.Launcher.Infrastructure.Launching;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dml.Launcher.Application.Tests;

public sealed class JavaLaunchTests
{
    [Theory]
    [InlineData("1.8.0_442", 8)]
    [InlineData("17.0.12", 17)]
    [InlineData("21.0.6+7-LTS", 21)]
    public void ParsesJavaMajorVersions(string version, int expected) =>
        Assert.Equal(expected, JavaRuntimeService.ParseMajorVersion(version));

    [Fact]
    public void ParsesJavaPropertiesAndBitness()
    {
        JavaInstallation installation = JavaRuntimeService.ParseInstallation(
            @"C:\Java\bin\java.exe",
            "java.version = 21.0.6\r\nos.arch = amd64\r\nsun.arch.data.model = 64");

        Assert.Equal(21, installation.MajorVersion);
        Assert.Equal("21.0.6", installation.Version);
        Assert.True(installation.Is64Bit);
    }

    [Fact]
    public void ParsesQuotedJvmArgumentsWithoutLosingSpaces()
    {
        IReadOnlyList<string> arguments = MinecraftGameLauncher.ParseCommandLine(
            "-XX:+UseG1GC -Dexample=plain \"-Dpath=C:\\A Folder\\file.txt\"");

        Assert.Equal(
            ["-XX:+UseG1GC", "-Dexample=plain", @"-Dpath=C:\A Folder\file.txt"],
            arguments);
    }

    [Fact]
    public async Task BuildsModernVanillaCommandAndExtractsWindowsNatives()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            const string Version = "test-1.0";
            string versionDirectory = Path.Combine(root, "versions", Version);
            string libraryPath = Path.Combine(root, "libraries", "com", "example", "core", "1.0", "core-1.0.jar");
            string nativePath = Path.Combine(root, "libraries", "com", "example", "native", "1.0", "native-1.0-natives-windows.jar");
            Directory.CreateDirectory(versionDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(libraryPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(nativePath)!);
            await File.WriteAllBytesAsync(Path.Combine(versionDirectory, Version + ".jar"), [1, 2, 3]);
            await File.WriteAllBytesAsync(libraryPath, [4, 5, 6]);
            using (ZipArchive archive = ZipFile.Open(nativePath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry("example.dll");
                await using (Stream stream = entry.Open())
                {
                    await stream.WriteAsync(new byte[] { 7, 8, 9 });
                }

                archive.CreateEntry("META-INF/MANIFEST.MF");
            }

            string metadata = $$"""
                {
                  "id": "{{Version}}",
                  "type": "release",
                  "mainClass": "net.minecraft.client.main.Main",
                  "assets": "test-assets",
                  "assetIndex": { "id": "test-assets" },
                  "javaVersion": { "majorVersion": 21 },
                  "arguments": {
                    "jvm": [
                      "-Djava.library.path=${natives_directory}",
                      { "rules": [{ "action": "allow", "os": { "name": "windows" } }], "value": ["-cp", "${classpath}"] }
                    ],
                    "game": [
                      "--username", "${auth_player_name}",
                      "--gameDir", "${game_directory}",
                      { "rules": [{ "action": "allow", "features": { "has_custom_resolution": true } }], "value": ["--width", "${resolution_width}", "--height", "${resolution_height}"] }
                    ]
                  },
                  "libraries": [
                    {
                      "name": "com.example:core:1.0",
                      "downloads": { "artifact": { "path": "com/example/core/1.0/core-1.0.jar" } }
                    },
                    {
                      "name": "com.example:native:1.0",
                      "natives": { "windows": "natives-windows" },
                      "downloads": { "classifiers": { "natives-windows": { "path": "com/example/native/1.0/native-1.0-natives-windows.jar" } } },
                      "extract": { "exclude": ["META-INF/"] }
                    }
                  ]
                }
                """;
            await File.WriteAllTextAsync(Path.Combine(versionDirectory, Version + ".json"), metadata);
            string javaDirectory = Path.Combine(root, "Java Home");
            Directory.CreateDirectory(javaDirectory);
            string javaPath = Path.Combine(javaDirectory, "java.exe");
            await File.WriteAllBytesAsync(javaPath, []);
            GameInstance instance = new(
                Guid.NewGuid(),
                "测试实例",
                Version,
                null,
                javaPath,
                root,
                4096,
                1600,
                900,
                null,
                InstanceInstallationState.Ready,
                "\"-Dcustom=value with spaces\"",
                false);
            FakeInstanceService instances = new(instance);
            MinecraftGameLauncher launcher = new(
                new FakeJavaRuntimeService(new JavaInstallation(javaPath, "21.0.6", 21, true)),
                instances,
                NullLogger<MinecraftGameLauncher>.Instance);

            GameLaunchCommand command = await launcher.BuildCommandAsync(instance);

            Assert.Equal(javaPath, command.JavaExecutablePath);
            Assert.Contains("-Xmx4096M", command.Arguments);
            Assert.Contains("-Dcustom=value with spaces", command.Arguments);
            Assert.Contains("net.minecraft.client.main.Main", command.Arguments);
            Assert.Contains("DML-Developer", command.Arguments);
            Assert.Contains("1600", command.Arguments);
            Assert.Contains("900", command.Arguments);
            Assert.Contains(command.Arguments, argument => argument.Contains(libraryPath, StringComparison.Ordinal));
            Assert.Contains(command.Arguments, argument => argument.Contains(Path.Combine(versionDirectory, Version + ".jar"), StringComparison.Ordinal));
            Assert.True(File.Exists(Path.Combine(root, "natives", Version, "example.dll")));
            Assert.False(File.Exists(Path.Combine(root, "natives", Version, "META-INF", "MANIFEST.MF")));
            Assert.Contains('"' + javaPath + '"', command.ToDisplayString(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "DmlLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class FakeJavaRuntimeService(JavaInstallation installation) : IJavaRuntimeService
    {
        public Task<IReadOnlyList<JavaInstallation>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JavaInstallation>>([installation]);

        public Task<JavaInstallation> InspectAsync(string executablePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(installation);
    }

    private sealed class FakeInstanceService(GameInstance instance) : IInstanceService
    {
        public Task<IReadOnlyList<GameInstance>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GameInstance>>([instance]);

        public Task<GameInstance> CreateAsync(string name, string minecraftVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GameInstance> CopyAsync(Guid sourceId, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RenameAsync(Guid id, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SetMinecraftVersionAsync(Guid id, string minecraftVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SetInstallationStateAsync(Guid id, InstanceInstallationState installationState, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task UpdateLaunchSettingsAsync(Guid id, string? javaPath, int memoryMegabytes, int width, int height, bool fullscreen, string? jvmArguments, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task MarkLaunchedAsync(Guid id, DateTimeOffset launchedAt, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GameInstance> ImportAsync(string sourceDirectory, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
