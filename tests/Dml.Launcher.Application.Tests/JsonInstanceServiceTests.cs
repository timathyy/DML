using Dml.Launcher.Domain.Instances;
using Dml.Launcher.Domain.Settings;
using Dml.Launcher.Infrastructure;
using Dml.Launcher.Infrastructure.Instances;
using Dml.Launcher.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dml.Launcher.Application.Tests;

public sealed class JsonInstanceServiceTests
{
    [Fact]
    public async Task CreateCopyAndRenameKeepInstancesInSeparateManagedDirectories()
    {
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "DmlLauncherTests", Guid.NewGuid().ToString("N"));
        try
        {
            AppPaths paths = new(temporaryDirectory, temporaryDirectory);
            JsonSettingsService settings = new(paths, NullLogger<JsonSettingsService>.Instance);
            await settings.SaveAsync(new LauncherSettings(Path.Combine(temporaryDirectory, "Game"), 4, null, LauncherTheme.System));
            JsonInstanceService service = new(paths, settings, NullLogger<JsonInstanceService>.Instance);
            var created = await service.CreateAsync("生存档", "1.21.1");
            Assert.Equal(InstanceInstallationState.Downloading, created.InstallationState);
            await service.SetInstallationStateAsync(created.Id, InstanceInstallationState.Paused);
            Assert.Equal(InstanceInstallationState.Paused, Assert.Single(await service.GetAllAsync()).InstallationState);
            await service.SetMinecraftVersionAsync(created.Id, "1.21.1");
            Assert.Equal(InstanceInstallationState.Ready, Assert.Single(await service.GetAllAsync()).InstallationState);
            string javaPath = Path.Combine(temporaryDirectory, "java.exe");
            await File.WriteAllBytesAsync(javaPath, []);
            await service.UpdateLaunchSettingsAsync(created.Id, javaPath, 6144, 1600, 900, true, "-XX:+UseG1GC");
            GameInstance configured = Assert.Single(await service.GetAllAsync());
            Assert.Equal(javaPath, configured.JavaPath);
            Assert.Equal(6144, configured.MemoryMegabytes);
            Assert.True(configured.Fullscreen);
            Assert.Equal("-XX:+UseG1GC", configured.JvmArguments);
            DateTimeOffset launchedAt = DateTimeOffset.UtcNow;
            await service.MarkLaunchedAsync(created.Id, launchedAt);
            Assert.Equal(launchedAt, Assert.Single(await service.GetAllAsync()).LastLaunchedAt);
            await File.WriteAllTextAsync(Path.Combine(created.GameDirectory, "options.txt"), "test");
            var copy = await service.CopyAsync(created.Id, "生存档副本");
            await service.RenameAsync(copy.Id, "冒险档");
            var all = await service.GetAllAsync();
            Assert.Equal(2, all.Count);
            Assert.NotEqual(created.GameDirectory, copy.GameDirectory);
            Assert.StartsWith(Path.Combine(temporaryDirectory, "Game", "Instances"), created.GameDirectory, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("冒险档", Assert.Single(all, item => item.Id == copy.Id).Name);
            Assert.Equal(InstanceInstallationState.Ready, copy.InstallationState);
            Assert.Equal("test", await File.ReadAllTextAsync(Path.Combine(copy.GameDirectory, "options.txt")));
        }
        finally { if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, recursive: true); }
    }

    [Fact]
    public async Task ImportCopiesSourceWithoutChangingIt()
    {
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "DmlLauncherTests", Guid.NewGuid().ToString("N"));
        try
        {
            string source = Path.Combine(temporaryDirectory, "ExistingMinecraft");
            Directory.CreateDirectory(source);
            await File.WriteAllTextAsync(Path.Combine(source, "marker.txt"), "source");
            AppPaths paths = new(temporaryDirectory, temporaryDirectory);
            JsonSettingsService settings = new(paths, NullLogger<JsonSettingsService>.Instance);
            await settings.SaveAsync(new LauncherSettings(Path.Combine(temporaryDirectory, "Game"), 4, null, LauncherTheme.System));
            JsonInstanceService service = new(paths, settings, NullLogger<JsonInstanceService>.Instance);
            var imported = await service.ImportAsync(source, "导入档");
            Assert.Equal("source", await File.ReadAllTextAsync(Path.Combine(source, "marker.txt")));
            Assert.Equal("source", await File.ReadAllTextAsync(Path.Combine(imported.GameDirectory, "marker.txt")));
        }
        finally { if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, recursive: true); }
    }
}
