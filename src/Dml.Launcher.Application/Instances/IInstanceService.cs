using Dml.Launcher.Domain.Instances;

namespace Dml.Launcher.Application.Instances;

public interface IInstanceService
{
    Task<IReadOnlyList<GameInstance>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<GameInstance> CreateAsync(string name, string minecraftVersion, CancellationToken cancellationToken = default);
    Task<GameInstance> CopyAsync(Guid sourceId, string name, CancellationToken cancellationToken = default);
    Task RenameAsync(Guid id, string name, CancellationToken cancellationToken = default);
    Task SetMinecraftVersionAsync(Guid id, string minecraftVersion, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<GameInstance> ImportAsync(string sourceDirectory, string name, CancellationToken cancellationToken = default);
}
