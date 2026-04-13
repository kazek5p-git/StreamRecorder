using System.Diagnostics;
using System.Text;

namespace StreamRecorder.WinForms.Services;

internal static class CrashGuard
{
    private const int MaxRestartAttempts = 3;
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(3);

    public static void StartDetached(string executablePath, IReadOnlyList<string> forwardedArgs, string logPath)
    {
        var startInfo = CreateProcessStartInfo(executablePath, ["--guard-mode", .. forwardedArgs]);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the crash guard process.");

        AppendLog(logPath, "Spawned crash guard.");
    }

    public static int Run(string executablePath, IReadOnlyList<string> childArgs, string logPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        AppendLog(logPath, "Crash guard started.");

        var restartCount = 0;
        while (true)
        {
            using var process = Process.Start(CreateChildStartInfo(executablePath, childArgs))
                ?? throw new InvalidOperationException("Failed to launch the guarded child process.");

            AppendLog(logPath, $"Guard launched child process {process.Id}.");
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                AppendLog(logPath, "Child exited cleanly, guard stopping.");
                return 0;
            }

            restartCount += 1;
            AppendLog(logPath, $"Child exited with code {process.ExitCode}, restart attempt {restartCount}.");

            if (restartCount >= MaxRestartAttempts)
            {
                AppendLog(logPath, "Too many restart attempts, guard stopping.");
                return process.ExitCode;
            }

            Thread.Sleep(RestartDelay);
        }
    }

    private static ProcessStartInfo CreateChildStartInfo(string executablePath, IReadOnlyList<string> childArgs)
    {
        return CreateProcessStartInfo(executablePath, ["--guarded", .. childArgs]);
    }

    private static ProcessStartInfo CreateProcessStartInfo(string executablePath, IReadOnlyList<string> args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = string.Join(" ", args.Select(EscapeArgument)),
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
        };

        return startInfo;
    }

    private static string EscapeArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        if (!value.Any(char.IsWhiteSpace) && !value.Contains('"'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static void AppendLog(string logPath, string message)
    {
        try
        {
            File.AppendAllText(
                logPath,
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
        }
    }
}
