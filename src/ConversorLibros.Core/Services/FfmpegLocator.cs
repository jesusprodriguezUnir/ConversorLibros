namespace ConversorLibros.Core.Services;

/// <summary>
/// Finds an ffmpeg executable: PATH first, then the WinGet package store and WinGet links folder.
/// Environment inputs are parameters so the lookup is testable without touching the real machine.
/// </summary>
public static class FfmpegLocator
{
    private const string ExecutableName = "ffmpeg.exe";

    /// <returns>The full path to ffmpeg, or <c>null</c> when none was found.</returns>
    public static string? Locate(string? pathVariable = null, string? localAppData = null)
    {
        pathVariable ??= Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var fromPath = FindOnPath(pathVariable);
        if (fromPath is not null)
        {
            return fromPath;
        }

        var wingetRoot = Path.Combine(localAppData, "Microsoft", "WinGet");
        var fromPackages = FindInWinGetPackages(Path.Combine(wingetRoot, "Packages"));
        if (fromPackages is not null)
        {
            return fromPackages;
        }

        var link = Path.Combine(wingetRoot, "Links", ExecutableName);
        return File.Exists(link) ? link : null;
    }

    private static string? FindOnPath(string pathVariable)
    {
        foreach (var rawDir in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dir = rawDir.Trim('"');
            if (dir.Length == 0)
            {
                continue;
            }

            try
            {
                var candidate = Path.Combine(dir, ExecutableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Malformed PATH entries are ignored.
            }
        }

        return null;
    }

    /// <summary>
    /// Looks for <c>Gyan.FFmpeg*\*\bin\ffmpeg.exe</c>. When several versions are installed the
    /// lexicographically greatest folder wins, so the result is deterministic.
    /// </summary>
    private static string? FindInWinGetPackages(string packagesRoot)
    {
        if (!Directory.Exists(packagesRoot))
        {
            return null;
        }

        return Directory.EnumerateDirectories(packagesRoot, "Gyan.FFmpeg*")
            .SelectMany(Directory.EnumerateDirectories)
            .Select(versionDir => Path.Combine(versionDir, "bin", ExecutableName))
            .Where(File.Exists)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
