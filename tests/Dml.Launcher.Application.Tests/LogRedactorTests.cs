using Dml.Launcher.Infrastructure.Logging;
using Xunit;

namespace Dml.Launcher.Application.Tests;

public sealed class LogRedactorTests
{
    [Fact]
    public void RedactReplacesTokenAndSensitiveQueryValues()
    {
        string result = LogRedactor.Redact("Bearer secret.value access_token=token-123&cookie=session-value");

        Assert.DoesNotContain("secret.value", result, StringComparison.Ordinal);
        Assert.DoesNotContain("token-123", result, StringComparison.Ordinal);
        Assert.DoesNotContain("session-value", result, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result, StringComparison.Ordinal);
    }
}
