namespace Dml.Launcher.Domain.Launching;

public sealed record JavaInstallation(
    string ExecutablePath,
    string Version,
    int MajorVersion,
    bool Is64Bit)
{
    public string DisplayName => $"Java {Version} ({(Is64Bit ? "64" : "32")} 位) — {ExecutablePath}";
}
