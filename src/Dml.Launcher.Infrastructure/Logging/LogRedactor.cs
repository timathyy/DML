using System.Text.RegularExpressions;

namespace Dml.Launcher.Infrastructure.Logging;

public static partial class LogRedactor
{
    public static string Redact(string value)
    {
        string redacted = BearerTokenRegex().Replace(value, "Bearer [REDACTED]");
        return SensitiveKeyValueRegex().Replace(redacted, "${key}=[REDACTED]");
    }

    [GeneratedRegex(@"(?i)Bearer\s+[A-Za-z0-9._~+\-/=]+")]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"(?i)(?<key>access_token|refresh_token|id_token|authorization_code|password|cookie)=[^\s&;]+")]
    private static partial Regex SensitiveKeyValueRegex();
}
