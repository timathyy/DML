using Dml.Launcher.Application.Downloads;
using Dml.Launcher.Domain.Downloads;
using Dml.Launcher.Domain.Instances;
using Dml.Launcher.Domain.Versions;

namespace Dml.Launcher.Application.Versions;

public interface IMinecraftVersionService
{
    Task<MinecraftVersionManifest> GetManifestAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);

    Task InstallAsync(
        GameInstance instance,
        MinecraftVersionInfo version,
        DownloadPauseController pauseController,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
