using System.Diagnostics;
using StreamRecorder.Core.Configuration;
using StreamRecorder.Core.Logging;

namespace StreamRecorder.Core.Tools;

public static class Mp4BoxRemuxer
{
    public static async Task<string> RemuxRawAacAsync(
        AppPaths paths,
        LogBus logs,
        string inputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logs);

        if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
        {
            return inputPath;
        }

        var toolPath = Mp4BoxLocator.ResolveExecutablePath(paths);
        if (string.IsNullOrWhiteSpace(toolPath) || !File.Exists(toolPath))
        {
            logs.Push("Skipping AAC to M4A remux: MP4Box.exe was not found");
            return inputPath;
        }

        var outputPath = EnsureUniqueOutputPath(Path.ChangeExtension(inputPath, ".m4a"));
        logs.Push($"AAC to M4A remux: {outputPath}");

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
        process.StartInfo.ArgumentList.Add("-add");
        process.StartInfo.ArgumentList.Add($"{inputPath}#audio");
        process.StartInfo.ArgumentList.Add("-new");
        process.StartInfo.ArgumentList.Add(outputPath);

        if (!process.Start())
        {
            logs.Push("AAC to M4A remux failed");
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
        logs.Push("AAC to M4A remux failed");
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
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }
}
