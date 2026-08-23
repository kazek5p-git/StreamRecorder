using System.Reflection;
using System.Text;
using StreamRecorder.Core;
using StreamRecorder.Core.Configuration;
using StreamRecorder.Core.Localization;
using StreamRecorder.WinForms.Forms;
using StreamRecorder.WinForms.Services;

namespace StreamRecorder.WinForms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var options = ProgramOptions.Parse(args);

        if (options.GuardMode)
        {
            var executablePath = ResolveExecutablePath();
            var logPath = Path.Combine(
                AppPaths.Discover(executablePath, installedOverride: options.InstalledOverride).ConfigDirectory,
                "crash_guard.log");
            return CrashGuard.Run(executablePath, options.ForwardedArgs, logPath);
        }

        var executable = ResolveExecutablePath();
        var paths = AppPaths.Discover(executable, installedOverride: options.InstalledOverride);
        var config = ConfigStore.LoadOrCreate(paths);
        AppLocalizer.ApplyThreadCulture(config.Settings.Language);

        if (options.ScheduledCommand is not null && ScheduledCommandServer.TrySend(options.ScheduledCommand))
        {
            return 0;
        }

        if (config.Settings.RestartOnCrash && !options.GuardedChild)
        {
            var logPath = Path.Combine(paths.ConfigDirectory, "crash_guard.log");
            CrashGuard.StartDetached(executable, options.ForwardedArgs, logPath);
            return 0;
        }

        if (options.CrashTest.Enabled)
        {
            return RunCrashTest(options.CrashTest, paths);
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        RegisterUnhandledExceptionLogging(paths);

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? Application.ProductVersion
            ?? "0.2.0-alpha3";

        using var app = new StreamRecorderApp(version, paths);
        Application.Run(new MainForm(app, options.ScheduledCommand, options.ScheduledMinimizedToTray));
        return 0;
    }

    private static int RunCrashTest(CrashTestOptions options, AppPaths paths)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(options.CountFilePath!)!);
        var runNumber = 1;
        if (File.Exists(options.CountFilePath))
        {
            var current = File.ReadAllText(options.CountFilePath, Encoding.UTF8).Trim();
            if (int.TryParse(current, out var parsed))
            {
                runNumber = parsed + 1;
            }
        }

        File.WriteAllText(options.CountFilePath!, runNumber.ToString(), Encoding.UTF8);

        var exitCode = runNumber <= options.CrashUntil ? options.CrashExitCode : 0;
        var logPath = Path.Combine(paths.ConfigDirectory, "crash_test.log");
        Directory.CreateDirectory(paths.ConfigDirectory);
        File.AppendAllText(
            logPath,
            $"[{DateTimeOffset.Now:O}] Crash test run {runNumber}, sleeping {options.ExitDelayMs} ms, exit code {exitCode}{Environment.NewLine}",
            Encoding.UTF8);

        Thread.Sleep(options.ExitDelayMs);
        return exitCode;
    }

    private static void RegisterUnhandledExceptionLogging(AppPaths paths)
    {
        var logPath = Path.Combine(paths.ConfigDirectory, "unhandled-crash.log");
        Directory.CreateDirectory(paths.ConfigDirectory);

        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            TryAppendCrashLine(logPath, "AppDomain.CurrentDomain.UnhandledException", eventArgs.ExceptionObject);
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            TryAppendCrashLine(logPath, "TaskScheduler.UnobservedTaskException", eventArgs.Exception);
        };
    }

    private static void TryAppendCrashLine(string logPath, string source, object? exceptionObject)
    {
        try
        {
            File.AppendAllText(
                logPath,
                $"[{DateTimeOffset.Now:O}] {source}: {exceptionObject}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static string ResolveExecutablePath()
    {
        return System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Unable to resolve the current executable path.");
    }

    private sealed class ProgramOptions
    {
        public bool GuardMode { get; set; }

        public bool GuardedChild { get; set; }

        public bool? InstalledOverride { get; set; }

        public ScheduledCommand? ScheduledCommand { get; set; }

        public bool ScheduledMinimizedToTray { get; set; }

        public IReadOnlyList<string> ForwardedArgs { get; set; } = [];

        public CrashTestOptions CrashTest { get; set; } = new();

        public static ProgramOptions Parse(IReadOnlyList<string> args)
        {
            var guardMode = false;
            var guardedChild = false;
            bool? installedOverride = null;
            string? crashCountFile = null;
            var crashUntil = 0;
            var exitDelayMs = 1000;
            var crashExitCode = 101;
            ScheduledCommand? scheduledCommand = null;
            var scheduledMinimizedToTray = false;
            var forwarded = new List<string>();

            for (var index = 0; index < args.Count; index++)
            {
                switch (args[index])
                {
                    case "--guard-mode":
                        guardMode = true;
                        break;
                    case "--guarded":
                        guardedChild = true;
                        break;
                    case "--installed":
                        installedOverride = true;
                        forwarded.Add("--installed");
                        break;
                    case "--test-crash-count-file":
                        crashCountFile = RequireValue(args, ref index);
                        forwarded.Add("--test-crash-count-file");
                        forwarded.Add(crashCountFile);
                        break;
                    case "--test-crash-until":
                        crashUntil = int.Parse(RequireValue(args, ref index));
                        forwarded.Add("--test-crash-until");
                        forwarded.Add(crashUntil.ToString());
                        break;
                    case "--test-exit-delay-ms":
                        exitDelayMs = int.Parse(RequireValue(args, ref index));
                        forwarded.Add("--test-exit-delay-ms");
                        forwarded.Add(exitDelayMs.ToString());
                        break;
                    case "--test-crash-exit-code":
                        crashExitCode = int.Parse(RequireValue(args, ref index));
                        forwarded.Add("--test-crash-exit-code");
                        forwarded.Add(crashExitCode.ToString());
                        break;
                    case "--scheduled-start":
                        var startScheduleId = Guid.Parse(RequireValue(args, ref index));
                        scheduledCommand = new ScheduledCommand(ScheduledCommandKind.Start, startScheduleId);
                        forwarded.Add("--scheduled-start");
                        forwarded.Add(startScheduleId.ToString("D"));
                        break;
                    case "--scheduled-stop":
                        var stopScheduleId = Guid.Parse(RequireValue(args, ref index));
                        scheduledCommand = new ScheduledCommand(ScheduledCommandKind.Stop, stopScheduleId);
                        forwarded.Add("--scheduled-stop");
                        forwarded.Add(stopScheduleId.ToString("D"));
                        break;
                    case "--scheduled-minimized-to-tray":
                        scheduledMinimizedToTray = true;
                        forwarded.Add("--scheduled-minimized-to-tray");
                        break;
                    default:
                        forwarded.Add(args[index]);
                        break;
                }
            }

            return new ProgramOptions
            {
                GuardMode = guardMode,
                GuardedChild = guardedChild,
                InstalledOverride = installedOverride,
                ScheduledCommand = scheduledCommand,
                ScheduledMinimizedToTray = scheduledMinimizedToTray,
                ForwardedArgs = forwarded,
                CrashTest = new CrashTestOptions
                {
                    CountFilePath = crashCountFile,
                    CrashUntil = crashUntil,
                    ExitDelayMs = exitDelayMs,
                    CrashExitCode = crashExitCode,
                },
            };
        }

        private static string RequireValue(IReadOnlyList<string> args, ref int index)
        {
            if (index + 1 >= args.Count)
            {
                throw new ArgumentException($"Missing value for {args[index]}");
            }

            index += 1;
            return args[index];
        }
    }

    private sealed class CrashTestOptions
    {
        public string? CountFilePath { get; set; }

        public int CrashUntil { get; set; }

        public int ExitDelayMs { get; set; } = 1000;

        public int CrashExitCode { get; set; } = 101;

        public bool Enabled => !string.IsNullOrWhiteSpace(CountFilePath);
    }
}
