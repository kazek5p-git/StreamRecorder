using System.Diagnostics;
using StreamRecorder.Core.Configuration;
using StreamRecorder.Core.Localization;
using StreamRecorder.Core.Logging;
using StreamRecorder.Core.Models;

namespace StreamRecorder.Core.Tools;

public static class Mp4BoxRemuxer
{
    public static async Task<string> RemuxRawAacAsync(
        AppPaths paths,
        LogBus logs,
        string languageCode,
        string inputPath,
        CancellationToken cancellationToken = default)
    {
        if (paths is null)
        {
            throw new ArgumentNullException(nameof(paths));
        }
        if (logs is null)
        {
            throw new ArgumentNullException(nameof(logs));
        }
        var localizer = AppLocalizer.For(languageCode, paths.RootDirectory);

        if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
        {
            return inputPath;
        }

        var toolPath = Mp4BoxLocator.ResolveExecutablePath(paths);
        if (string.IsNullOrWhiteSpace(toolPath) || !File.Exists(toolPath))
        {
            logs.Push(localizer.RemuxSkippingMp4BoxMissing);
            return inputPath;
        }

        var outputPath = EnsureUniqueOutputPath(Path.ChangeExtension(inputPath, ".m4a"));
        logs.Push(localizer.RemuxStarted(outputPath));

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = toolPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(inputPath) ?? paths.RootDirectory,
            },
        };
        process.StartInfo.Arguments = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "-add \"{0}#audio\" -new \"{1}\"",
            inputPath.Replace("\"", "\\\""),
            outputPath.Replace("\"", "\\\""));

        if (!process.Start())
        {
            logs.Push(localizer.RemuxFailed);
            return inputPath;
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        if (process.ExitCode == 0 && File.Exists(outputPath))
        {
            TryDelete(inputPath);
            return outputPath;
        }

        TryDelete(outputPath);
        logs.Push(localizer.RemuxFailed);
        return inputPath;
    }

    private static string EnsureUniqueOutputPath(string path)
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

    private static void TryDelete(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch
        {
        }
    }
}
