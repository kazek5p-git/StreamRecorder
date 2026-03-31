using System.Diagnostics;
using StreamRecorder.Core;
using StreamRecorder.Core.Configuration;
using StreamRecorder.Core.Models;

return await ProgramMainAsync(args);

static async Task<int> ProgramMainAsync(string[] args)
{
    var options = CrashParityOptions.Parse(args);
    var repoRoot = options.RepoRoot ?? Directory.GetCurrentDirectory();
    var sourceBuildDir = Path.Combine(repoRoot, "dotnet", "src", "StreamRecorder.WinForms", "bin", "Release", "net8.0-windows");
    var sessionRoot = options.OutputRoot ?? Path.Combine(repoRoot, "dotnet", "target", "parity-crash", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
    Directory.CreateDirectory(sessionRoot);

    var result = new CrashParityResult
    {
        SessionRoot = sessionRoot,
        SourceBuildDirectory = sourceBuildDir,
    };

    try
    {
        result.DisabledCase = await RunCaseAsync(
            sourceBuildDir,
            Path.Combine(sessionRoot, "restart-disabled"),
            restartOnCrash: false,
            label: "restart-disabled");

        result.EnabledCase = await RunCaseAsync(
            sourceBuildDir,
            Path.Combine(sessionRoot, "restart-enabled"),
            restartOnCrash: true,
            label: "restart-enabled");

        result.Pass = result.DisabledCase.Pass && result.EnabledCase.Pass;
    }
    catch (Exception ex)
    {
        result.Exception = ex.ToString();
        result.Pass = false;
    }

    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
    return result.Pass ? 0 : 1;
}

static async Task<CrashCaseResult> RunCaseAsync(string sourceBuildDir, string installRoot, bool restartOnCrash, string label)
{
    Directory.CreateDirectory(installRoot);
    CopyBuildFiles(sourceBuildDir, installRoot);

    var executablePath = Path.Combine(installRoot, "StreamRecorder.exe");
    var paths = AppPaths.Discover(executablePath);
    var config = new AppConfig
    {
        Settings = new AppSettings
        {
            ConfirmOnExit = false,
            RestartOnCrash = restartOnCrash,
            MinimizeToTray = false,
            StartMinimized = false,
            RecordingsFolder = AppDefaults.DefaultRecordingsFolder,
            FileNameTemplate = AppDefaults.DefaultFileNameTemplate,
            Language = Language.English,
        },
    };
    ConfigStore.Save(paths, config);

    var countFile = Path.Combine(paths.ConfigDirectory, "crash-count.txt");
    var process = Process.Start(new ProcessStartInfo
    {
        FileName = executablePath,
        UseShellExecute = false,
        CreateNoWindow = true,
        WorkingDirectory = installRoot,
        ArgumentList =
        {
            "--test-crash-count-file", countFile,
            "--test-crash-until", "1",
            "--test-exit-delay-ms", "600",
        },
    }) ?? throw new InvalidOperationException("Failed to start the crash parity test process.");

    var result = new CrashCaseResult
    {
        Label = label,
        InstallRoot = installRoot,
        RestartOnCrash = restartOnCrash,
        InitialProcessId = process.Id,
    };

    if (!process.WaitForExit(15000))
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
        }

        result.Exception = "Timed out waiting for the initial process to exit.";
        result.Pass = false;
        return result;
    }

    result.InitialExitCode = process.ExitCode;

    var expectedRuns = restartOnCrash ? 2 : 1;
    result.RunCount = await WaitForRunCountAsync(countFile, expectedRuns, TimeSpan.FromSeconds(25));
    result.CrashLogPath = Path.Combine(paths.ConfigDirectory, "crash_test.log");
    result.CrashLogTail = ReadTail(result.CrashLogPath);

    if (restartOnCrash)
    {
        result.GuardLogPath = Path.Combine(paths.ConfigDirectory, "crash_guard.log");
        await WaitForLogLineAsync(result.GuardLogPath, "Child exited cleanly, guard stopping.", TimeSpan.FromSeconds(25));
        result.GuardLogTail = ReadTail(result.GuardLogPath);
        result.Pass =
            result.InitialExitCode == 0 &&
            result.RunCount == 2 &&
            result.GuardLogTail.Any(static line => line.Contains("restart attempt 1", StringComparison.OrdinalIgnoreCase)) &&
            result.GuardLogTail.Any(static line => line.Contains("Child exited cleanly, guard stopping.", StringComparison.Ordinal));
    }
    else
    {
        result.Pass =
            result.InitialExitCode == 101 &&
            result.RunCount == 1 &&
            !File.Exists(Path.Combine(paths.ConfigDirectory, "crash_guard.log"));
    }

    return result;
}

static void CopyBuildFiles(string sourceBuildDir, string installRoot)
{
    foreach (var file in Directory.GetFiles(sourceBuildDir))
    {
        var name = Path.GetFileName(file);
        File.Copy(file, Path.Combine(installRoot, name), overwrite: true);
    }
}

static async Task<int> WaitForRunCountAsync(string countFile, int expectedMinimum, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        var current = ReadRunCount(countFile);
        if (current >= expectedMinimum)
        {
            return current;
        }

        await Task.Delay(250);
    }

    return ReadRunCount(countFile);
}

static int ReadRunCount(string countFile)
{
    if (!File.Exists(countFile))
    {
        return 0;
    }

    var text = File.ReadAllText(countFile).Trim();
    return int.TryParse(text, out var parsed) ? parsed : 0;
}

static async Task<bool> WaitForLogLineAsync(string logPath, string fragment, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        if (File.Exists(logPath))
        {
            var lines = await File.ReadAllLinesAsync(logPath);
            if (lines.Any(line => line.Contains(fragment, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        await Task.Delay(250);
    }

    return false;
}

static List<string> ReadTail(string? path)
{
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
    {
        return [];
    }

    return File.ReadAllLines(path).TakeLast(20).ToList();
}

internal sealed class CrashParityOptions
{
    public string? RepoRoot { get; init; }

    public string? OutputRoot { get; init; }

    public static CrashParityOptions Parse(IReadOnlyList<string> args)
    {
        string? repoRoot = null;
        string? outputRoot = null;

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--repo-root":
                    repoRoot = RequireValue(args, ref index);
                    break;
                case "--output-root":
                    outputRoot = RequireValue(args, ref index);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[index]}");
            }
        }

        return new CrashParityOptions
        {
            RepoRoot = repoRoot,
            OutputRoot = outputRoot,
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

internal sealed class CrashParityResult
{
    public string SessionRoot { get; set; } = string.Empty;

    public string SourceBuildDirectory { get; set; } = string.Empty;

    public CrashCaseResult DisabledCase { get; set; } = new();

    public CrashCaseResult EnabledCase { get; set; } = new();

    public string? Exception { get; set; }

    public bool Pass { get; set; }
}

internal sealed class CrashCaseResult
{
    public string Label { get; set; } = string.Empty;

    public string InstallRoot { get; set; } = string.Empty;

    public bool RestartOnCrash { get; set; }

    public int InitialProcessId { get; set; }

    public int InitialExitCode { get; set; }

    public int RunCount { get; set; }

    public string? CrashLogPath { get; set; }

    public List<string> CrashLogTail { get; set; } = [];

    public string? GuardLogPath { get; set; }

    public List<string> GuardLogTail { get; set; } = [];

    public string? Exception { get; set; }

    public bool Pass { get; set; }
}
