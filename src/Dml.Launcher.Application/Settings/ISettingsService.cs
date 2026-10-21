using Dml.Launcher.Domain.Settings;

namespace Dml.Launcher.Application.Settings;

public interface ISettingsService
{
    LauncherSettings Current { get; }

    Task LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default);
}
