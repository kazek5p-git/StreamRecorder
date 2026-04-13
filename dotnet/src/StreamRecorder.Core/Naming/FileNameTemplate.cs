using StreamRecorder.Core.Configuration;
using StreamRecorder.Core.Models;

namespace StreamRecorder.Core.Naming;

public static class FileNameTemplate
{
    public static string ResolveRecordingsDirectory(AppPaths paths, AppSettings settings)
    {
        if (paths is null)
        {
            throw new ArgumentNullException(nameof(paths));
        }
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var folder = Path.IsPathRooted(settings.RecordingsFolder)
            ? settings.RecordingsFolder
            : Path.Combine(paths.RootDirectory, settings.RecordingsFolder);

        Directory.CreateDirectory(folder);
        return folder;
    }

    public static string BuildOutputPath(
        AppPaths paths,
        AppSettings settings,
        Station station,
        string extension,
        DateTimeOffset now)
    {
        if (paths is null)
        {
            throw new ArgumentNullException(nameof(paths));
        }
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }
        if (station is null)
        {
            throw new ArgumentNullException(nameof(station));
        }

        var recordingsDirectory = ResolveRecordingsDirectory(paths, settings);
        var fileName = ApplyTemplate(settings.FileNameTemplate, station, now);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = SanitizeFileName(station.Name);
        }

        fileName = SanitizeFileName(fileName);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            fileName = $"{fileName}.{extension}";
        }

        return EnsureUnique(Path.Combine(recordingsDirectory, fileName));
    }

    public static string SanitizeFileName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "recording";
        }

        Span<char> invalidChars = stackalloc char[]
        {
            '<', '>', ':', '"', '/', '\\', '|', '?', '*',
        };

        var sanitized = new char[input.Length];
        for (var index = 0; index < input.Length; index++)
        {
            var ch = input[index];
            sanitized[index] = char.IsControl(ch) || invalidChars.ToArray().Contains(ch) ? '_' : ch;
        }

        var value = new string(sanitized).Trim('.', ' ');
        if (value.Length > 160)
        {
            value = value.Substring(0, 160);
        }

        value = value.Trim();
        return string.IsNullOrWhiteSpace(value) ? "recording" : value;
    }

    private static string ApplyTemplate(string template, Station station, DateTimeOffset now)
    {
        var stationName = SanitizeFileName(station.Name);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["%t"] = stationName,
            ["%r"] = now.ToString("yyyy"),
            ["%n"] = now.ToString("MM"),
            ["%M"] = now.ToString("MM"),
            ["%d"] = now.ToString("dd"),
            ["%h"] = now.ToString("HH"),
            ["%m"] = now.ToString("mm"),
            ["%s"] = now.ToString("ss"),
        };

        var result = template ?? string.Empty;
        foreach (var pair in values)
        {
            result = result.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        }

        return result;
    }

    private static string EnsureUnique(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 1; index < 10000; index++)
        {
            var candidate = Path.Combine(directory, $"{stem}_{index}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return path;
    }
}
