using StreamRecorder.Core.Configuration;

namespace StreamRecorder.Core.Tools;

public static class Mp4BoxLocator
{
    public static string? ResolveExecutablePath(AppPaths paths)
    {
        return ResolveExecutablePath(paths, GetProgramFilesRoots(), EnumerateDirectoriesSafe, File.Exists);
    }

    public static string? ResolveExecutablePath(
        AppPaths paths,
        IEnumerable<string> programFilesRoots,
        Func<string, IEnumerable<string>> enumerateDirectories,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);

        return EnumerateCandidates(paths, programFilesRoots, enumerateDirectories)
            .FirstOrDefault(fileExists);
    }

    public static IReadOnlyList<string> EnumerateCandidates(AppPaths paths)
    {
        return EnumerateCandidates(paths, GetProgramFilesRoots(), EnumerateDirectoriesSafe);
    }

    public static IReadOnlyList<string> EnumerateCandidates(
        AppPaths paths,
        IEnumerable<string> programFilesRoots,
        Func<string, IEnumerable<string>> enumerateDirectories)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(programFilesRoots);
        ArgumentNullException.ThrowIfNull(enumerateDirectories);

        var candidates = new List<string>
        {
            Path.Combine(paths.RootDirectory, "Tools", "GPAC", "MP4Box.exe"),
            Path.Combine(paths.RootDirectory, "Tools", "MP4Box.exe"),
            Path.Combine(paths.RootDirectory, "MP4Box.exe"),
        };

        foreach (var root in programFilesRoots.Where(static path => !string.IsNullOrWhiteSpace(path)))
        {
            candidates.Add(Path.Combine(root, "GPAC", "MP4Box.exe"));

            foreach (var directory in enumerateDirectories(root))
            {
                var name = Path.GetFileName(directory);
                if (string.IsNullOrWhiteSpace(name) || !name.StartsWith("gpac", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                candidates.Add(Path.Combine(directory, "MP4Box.exe"));
                candidates.Add(Path.Combine(directory, "gpac", "MP4Box.exe"));
            }
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> GetProgramFilesRoots()
    {
        foreach (var variable in new[] { "ProgramFiles", "ProgramFiles(x86)" })
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
