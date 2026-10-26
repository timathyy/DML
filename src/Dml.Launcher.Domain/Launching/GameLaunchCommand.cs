namespace Dml.Launcher.Domain.Launching;

public sealed record GameLaunchCommand(
    string JavaExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory)
{
    public string ToDisplayString() => string.Join(' ', [Quote(JavaExecutablePath), .. Arguments.Select(Quote)]);

    private static string Quote(string value)
    {
        if (value.Length > 0 && !value.Any(character => char.IsWhiteSpace(character) || character == '"'))
        {
            return value;
        }

        System.Text.StringBuilder result = new(value.Length + 2);
        result.Append('"');
        int backslashes = 0;
        foreach (char character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                result.Append('\\', backslashes * 2 + 1);
                result.Append('"');
                backslashes = 0;
                continue;
            }

            result.Append('\\', backslashes);
            backslashes = 0;
            result.Append(character);
        }

        result.Append('\\', backslashes * 2);
        result.Append('"');
        return result.ToString();
    }
}
