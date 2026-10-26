using Dml.Launcher.Domain.Launching;

namespace Dml.Launcher.Application.Launching;

public interface IJavaRuntimeService
{
    Task<IReadOnlyList<JavaInstallation>> DiscoverAsync(CancellationToken cancellationToken = default);

    Task<JavaInstallation> InspectAsync(string executablePath, CancellationToken cancellationToken = default);
}
