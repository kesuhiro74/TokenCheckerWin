namespace TokenChecker.Core.Providers;

internal static class CommandLineProbe
{
    public static bool ExistsOnPath(string commandName)
        => TryFindOnPath(commandName, out _);

    public static bool TryFindOnPath(string commandName, out string commandPath)
    {
        commandPath = string.Empty;
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extensions = OperatingSystem.IsWindows()
            ? GetPathExtensions()
            : new[] { string.Empty };

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, commandName + extension);
                if (File.Exists(candidate))
                {
                    commandPath = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private static string[] GetPathExtensions()
    {
        var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(pathExt))
        {
            return new[] { ".exe", ".cmd", ".bat", string.Empty };
        }

        return pathExt
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
