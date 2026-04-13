using StreamRecorder.Core;
using StreamRecorder.Core.Configuration;
using StreamRecorder.Core.Updates;

return await ProgramMainAsync(args);

static async Task<int> ProgramMainAsync(string[] args)
{
    var options = UpdaterParityOptions.Parse(args);
    var rootDirectory = options.OutputRoot ?? Path.Combine(Path.GetTempPath(), "StreamRecorderUpdaterParity", Guid.NewGuid().ToString("N"));
    var paths = new AppPaths
    {
        RootDirectory = rootDirectory,
        ConfigDirectory = Path.Combine(rootDirectory, AppDefaults.ConfigDirectoryName),
        RecordingsDirectory = Path.Combine(rootDirectory, AppDefaults.DefaultRecordingsFolder),
        ConfigFilePath = Path.Combine(rootDirectory, AppDefaults.ConfigDirectoryName, AppDefaults.ConfigFileName),
        LogFilePath = Path.Combine(rootDirectory, AppDefaults.ConfigDirectoryName, AppDefaults.LogFileName),
    };
    paths.EnsureDirectories();

    var result = new UpdaterParityResult
    {
        RootDirectory = rootDirectory,
        Repository = AppDefaults.DefaultUpdateRepository,
    };

    try
    {
        var updater = new UpdaterService("0.2.0-alpha3-updater");

        result.UpdateFromOldVersion = await updater.CheckForUpdatesAsync("0.1.6.2");
        result.NoDowngradeForPreviewBuild = await updater.CheckForUpdatesAsync("0.2.0-alpha3") is null;
        result.NoUpdateForCurrentStable = await updater.CheckForUpdatesAsync("0.1.6.3") is null;

        if (result.UpdateFromOldVersion?.Asset is null)
        {
            throw new InvalidOperationException("Expected an update asset for version 0.1.6.2, but none was returned.");
        }

        result.DownloadedAssetPath = await updater.DownloadUpdateAsync(paths, result.UpdateFromOldVersion);
        result.DownloadedAssetExists = File.Exists(result.DownloadedAssetPath);
        result.DownloadedAssetSize = result.DownloadedAssetExists ? new FileInfo(result.DownloadedAssetPath).Length : 0;

        var restartExecutable = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        await updater.InstallDownloadedUpdateAsync(
            paths,
            result.DownloadedAssetPath,
            result.UpdateFromOldVersion.Asset,
            restartExecutable,
            ["/d", "/c", "exit 0"]);

        var scriptPath = Path.Combine(paths.ConfigDirectory, "updates", "apply_update.ps1");
        var logPath = Path.Combine(paths.ConfigDirectory, "updates", "apply_update.log");
        result.InstallScriptCreated = await WaitForFileAsync(scriptPath, TimeSpan.FromSeconds(10));
        result.InstallLogCreated = await WaitForFileAsync(logPath, TimeSpan.FromSeconds(45));

        result.AppliedExecutableExists = await WaitForFileAsync(Path.Combine(rootDirectory, "streamrecorder.exe"), TimeSpan.FromSeconds(45));
        result.ReadmeExists = await WaitForFileAsync(Path.Combine(rootDirectory, "README.html"), TimeSpan.FromSeconds(45));
        await WaitForLogEntryAsync(logPath, "Restarted application", TimeSpan.FromSeconds(10));
        result.InstallLogTail = result.InstallLogCreated
            ? ReadTail(logPath, 10)
            : [];

        result.InstallSucceeded =
            result.InstallScriptCreated &&
            result.InstallLogCreated &&
            result.AppliedExecutableExists &&
            result.ReadmeExists &&
            result.InstallLogTail.Any(static line => Contains(line, "Update files copied successfully", StringComparison.Ordinal))
            && result.InstallLogTail.Any(static line => Contains(line, "Restarted application", StringComparison.Ordinal));

        result.Pass =
            result.UpdateFromOldVersion is not null &&
            result.UpdateFromOldVersion.Asset.Kind == UpdateAssetKind.Zip &&
            result.NoDowngradeForPreviewBuild &&
            result.NoUpdateForCurrentStable &&
            result.DownloadedAssetExists &&
            result.DownloadedAssetSize > 0 &&
            result.InstallSucceeded;
    }
    catch (Exception ex)
    {
        result.Exception = ex.ToString();
        result.Pass = false;
    }

    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
    return result.Pass ? 0 : 1;
}

static async Task<bool> WaitForFileAsync(string path, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        if (File.Exists(path))
        {
            return true;
        }

        await Task.Delay(250);
    }

    return false;
}

static async Task<bool> WaitForLogEntryAsync(string path, string fragment, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        if (File.Exists(path))
        {
            var lines = await Task.Run(() => File.ReadAllLines(path));
            if (lines.Any(line => Contains(line, fragment, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        await Task.Delay(250);
    }

    return false;
}

static List<string> ReadTail(string path, int count)
{
    if (!File.Exists(path))
    {
        return [];
    }

    var lines = File.ReadAllLines(path);
    var skip = Math.Max(0, lines.Length - count);
    return lines.Skip(skip).ToList();
}

static bool Contains(string value, string comparisonValue, StringComparison comparison)
{
    return value?.IndexOf(comparisonValue, comparison) >= 0;
}

internal sealed class UpdaterParityOptions
{
    public string? OutputRoot { get; set; }

    public static UpdaterParityOptions Parse(IReadOnlyList<string> args)
    {
        string? outputRoot = null;

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--output-root":
                    outputRoot = RequireValue(args, ref index);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[index]}");
            }
        }

        return new UpdaterParityOptions
        {
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

internal sealed class UpdaterParityResult
{
    public string RootDirectory { get; set; } = string.Empty;

    public string Repository { get; set; } = string.Empty;

    public UpdateInfo? UpdateFromOldVersion { get; set; }

    public bool NoDowngradeForPreviewBuild { get; set; }

    public bool NoUpdateForCurrentStable { get; set; }

    public string DownloadedAssetPath { get; set; } = string.Empty;

    public bool DownloadedAssetExists { get; set; }

    public long DownloadedAssetSize { get; set; }

    public bool InstallScriptCreated { get; set; }

    public bool InstallLogCreated { get; set; }

    public bool AppliedExecutableExists { get; set; }

    public bool ReadmeExists { get; set; }

    public bool InstallSucceeded { get; set; }

    public List<string> InstallLogTail { get; set; } = [];

    public string? Exception { get; set; }

    public bool Pass { get; set; }
}
