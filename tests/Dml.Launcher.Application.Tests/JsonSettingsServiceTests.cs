using Dml.Launcher.Domain.Settings;
using Dml.Launcher.Infrastructure;
using Dml.Launcher.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dml.Launcher.Application.Tests;

public sealed class JsonSettingsServiceTests
{
    [Fact]
    public async Task SaveThenLoadPreservesSettings()
    {
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "DmlLauncherTests", Guid.NewGuid().ToString("N"));
        try
        {
            AppPaths paths = new(temporaryDirectory, temporaryDirectory);
            LauncherSettings expected = new(
                Path.Combine(temporaryDirectory, "Game"),
                8,
                "http://127.0.0.1:7890",
                LauncherTheme.Dark);
            JsonSettingsService writer = new(paths, NullLogger<JsonSettingsService>.Instance);

            await writer.SaveAsync(expected);

            JsonSettingsService reader = new(paths, NullLogger<JsonSettingsService>.Instance);
            await reader.LoadAsync();

            Assert.Equal(expected, reader.Current);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }
}
