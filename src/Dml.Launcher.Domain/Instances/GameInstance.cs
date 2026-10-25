namespace Dml.Launcher.Domain.Instances;

public sealed record GameInstance(
    Guid Id,
    string Name,
    string MinecraftVersion,
    string? Loader,
    string? JavaPath,
    string GameDirectory,
    int MemoryMegabytes,
    int Width,
    int Height,
    DateTimeOffset? LastLaunchedAt,
    InstanceInstallationState InstallationState = InstanceInstallationState.Ready);
