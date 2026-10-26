using Dml.Launcher.Domain.Instances;
using Dml.Launcher.Domain.Launching;

namespace Dml.Launcher.Application.Launching;

public interface IGameLauncher
{
    Task<GameLaunchCommand> BuildCommandAsync(GameInstance instance, CancellationToken cancellationToken = default);

    Task<GameLaunchResult> LaunchAsync(
        GameInstance instance,
        Action<string>? output = null,
        CancellationToken cancellationToken = default);
}
