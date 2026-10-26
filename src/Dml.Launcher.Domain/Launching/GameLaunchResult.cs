namespace Dml.Launcher.Domain.Launching;

public sealed record GameLaunchResult(
    int ExitCode,
    DateTimeOffset StartedAt,
    DateTimeOffset ExitedAt,
    string LogPath);
