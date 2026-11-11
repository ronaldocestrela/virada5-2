namespace Infrastructure.Data;

/// <summary>
/// Utilitário para carregamento seguro de variáveis de ambiente a partir de arquivo <c>.env</c>
/// sem sobrescrever variáveis já definidas no ambiente do processo.
/// </summary>
public static class DotEnvLoader
{
    public static void LoadIfPresent(string? startDirectory = null)
    {
        var envFilePath = FindEnvFile(startDirectory ?? Directory.GetCurrentDirectory());
        if (envFilePath is null)
        {
            return;
        }

        foreach (var (key, value) in ParseFile(envFilePath))
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    public static string? ResolveValue(string key, string? startDirectory = null)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        var envFilePath = FindEnvFile(startDirectory ?? Directory.GetCurrentDirectory());
        if (envFilePath is null)
        {
            return null;
        }

        foreach (var (entryKey, entryValue) in ParseFile(envFilePath))
        {
            if (string.Equals(entryKey, key, StringComparison.Ordinal))
            {
                return string.IsNullOrWhiteSpace(entryValue) ? null : entryValue;
            }
        }

        return null;
    }

    private static string? FindEnvFile(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            var envFilePath = Path.Combine(directory.FullName, ".env");
            if (File.Exists(envFilePath))
            {
                return envFilePath;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static IEnumerable<(string Key, string Value)> ParseFile(string envFilePath)
    {
        foreach (var rawLine in File.ReadLines(envFilePath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            var value = line[(separatorIndex + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            yield return (key, value);
        }
    }
}

