using System.Text.Json;
using StreamRecorder.Core;
using StreamRecorder.Core.Configuration;
using StreamRecorder.Core.Logging;
using StreamRecorder.Core.Models;
using StreamRecorder.Core.Recording;

return await ProgramMainAsync(args);

static async Task<int> ProgramMainAsync(string[] args)
{
    var options = RunnerOptions.Parse(args);
    var rootDirectory = options.OutputRoot ?? Path.Combine(Path.GetTempPath(), "StreamRecorderParity", Sanitize(options.Name), Guid.NewGuid().ToString("N"));
    var paths = new AppPaths
    {
        RootDirectory = rootDirectory,
        ConfigDirectory = Path.Combine(rootDirectory, AppDefaults.ConfigDirectoryName),
        RecordingsDirectory = Path.Combine(rootDirectory, AppDefaults.DefaultRecordingsFolderName),
        ConfigFilePath = Path.Combine(rootDirectory, AppDefaults.ConfigDirectoryName, AppDefaults.ConfigFileName),
        LogFilePath = Path.Combine(rootDirectory, AppDefaults.ConfigDirectoryName, AppDefaults.LogFileName),
    };
    paths.EnsureDirectories();

    var settings = new AppSettings
    {
        RecordingsFolder = string.IsNullOrWhiteSpace(options.RecordingsFolder)
            ? AppDefaults.DefaultRecordingsFolderName
            : options.RecordingsFolder!,
        FileNameTemplate = string.IsNullOrWhiteSpace(options.FileNameTemplate)
            ? AppDefaults.DefaultFileNameTemplate
            : options.FileNameTemplate!,
        RemuxRawAacToM4A = options.RemuxRawAacToM4A,
        Language = LanguageCodes.English,
    };

    var logs = new LogBus(paths.LogFilePath);
    using var recorder = new RecordingService("0.2.0-alpha3-parity");
    var station = new Station
    {
        Id = Guid.NewGuid(),
        Name = options.Name,
        Url = options.Url,
        Credentials = string.IsNullOrWhiteSpace(options.Username)
            ? null
            : new Credentials
            {
                Username = options.Username!,
                Password = options.Password ?? string.Empty,
            },
    };

    var result = new RecordingParityResult
    {
        Name = options.Name,
        Url = options.Url,
        ExpectedFormat = options.ExpectedFormat?.ToString(),
        RootDirectory = rootDirectory,
        RequestedDurationSeconds = options.DurationSeconds,
        StartupTimeoutSeconds = options.StartupTimeoutSeconds,
        RemuxRawAacToM4A = options.RemuxRawAacToM4A,
    };

    try
    {
        await recorder.StartAsync(station, settings, paths, logs);

        var started = await WaitForRecordingStartAsync(recorder, station.Id, TimeSpan.FromSeconds(options.StartupTimeoutSeconds));
        result.Started = started;
        result.StartSnapshot = SnapshotReport.FromSnapshot(recorder.GetSnapshot(station.Id));

        if (started)
        {
            await Task.Delay(TimeSpan.FromSeconds(options.DurationSeconds));
        }

        recorder.Stop(station.Id);
        var stopped = await WaitForRecordingStopAsync(recorder, station.Id, TimeSpan.FromSeconds(20));
        result.Stopped = stopped;
        result.FinalSnapshot = SnapshotReport.FromSnapshot(recorder.GetSnapshot(station.Id));

        if (!string.IsNullOrWhiteSpace(result.FinalSnapshot?.OutputPath))
        {
            result.Output = BuildOutputReport(result.FinalSnapshot.OutputPath!);
        }

        result.LogLines = logs.Entries
            .Select(static entry => entry.FormatLine())
            .ToList();
        result.Pass = EvaluatePass(result, options.ExpectedFormat);
    }
    catch (Exception ex)
    {
        result.Exception = ex.ToString();
        result.LogLines = logs.Entries
            .Select(static entry => entry.FormatLine())
            .ToList();
        result.Pass = false;
    }

    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions
    {
        WriteIndented = false,
    }));

    return result.Pass ? 0 : 1;
}

static async Task<bool> WaitForRecordingStartAsync(RecordingService recorder, Guid stationId, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        var snapshot = recorder.GetSnapshot(stationId);
        if (snapshot is not null)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.LastError))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.OutputPath) && snapshot.BytesWritten > 0)
            {
                return true;
            }
        }

        await Task.Delay(250);
    }

    return false;
}

static async Task<bool> WaitForRecordingStopAsync(RecordingService recorder, Guid stationId, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        var snapshot = recorder.GetSnapshot(stationId);
        if (snapshot is not null && !snapshot.Active && string.Equals(snapshot.StateLabel, "Stopped", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        await Task.Delay(250);
    }

    return false;
}

static OutputReport? BuildOutputReport(string outputPath)
{
    if (string.IsNullOrWhiteSpace(outputPath))
    {
        return null;
    }

    var exists = File.Exists(outputPath);
    return new OutputReport
    {
        Path = outputPath,
        Exists = exists,
        Extension = Path.GetExtension(outputPath),
        Length = exists ? new FileInfo(outputPath).Length : 0,
    };
}

static bool EvaluatePass(RecordingParityResult result, StreamFormat? expectedFormat)
{
    if (!result.Started || !result.Stopped)
    {
        return false;
    }

    if (result.Output is null || !result.Output.Exists || result.Output.Length <= 0)
    {
        return false;
    }

    if (result.FinalSnapshot is null || !string.Equals(result.FinalSnapshot.StateLabel, "Stopped", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (expectedFormat is not null && !string.Equals(result.FinalSnapshot.Format, expectedFormat.ToString(), StringComparison.Ordinal))
    {
        return false;
    }

    return true;
}

static string Sanitize(string value)
{
    var invalid = Path.GetInvalidFileNameChars();
    return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
}

internal sealed class RunnerOptions
{
    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public StreamFormat? ExpectedFormat { get; set; }

    public int DurationSeconds { get; set; } = 8;

    public int StartupTimeoutSeconds { get; set; } = 20;

    public string? OutputRoot { get; set; }

    public bool RemuxRawAacToM4A { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? RecordingsFolder { get; set; }

    public string? FileNameTemplate { get; set; }

    public static RunnerOptions Parse(IReadOnlyList<string> args)
    {
        string? name = null;
        string? url = null;
        StreamFormat? expectedFormat = null;
        var durationSeconds = 8;
        var startupTimeoutSeconds = 20;
        string? outputRoot = null;
        var remux = false;
        string? username = null;
        string? password = null;
        string? recordingsFolder = null;
        string? fileNameTemplate = null;

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--name":
                    name = RequireValue(args, ref index);
                    break;
                case "--url":
                    url = RequireValue(args, ref index);
                    break;
                case "--expected-format":
                    expectedFormat = (StreamFormat)Enum.Parse(typeof(StreamFormat), RequireValue(args, ref index), ignoreCase: true);
                    break;
                case "--duration-seconds":
                    durationSeconds = int.Parse(RequireValue(args, ref index));
                    break;
                case "--startup-timeout-seconds":
                    startupTimeoutSeconds = int.Parse(RequireValue(args, ref index));
                    break;
                case "--output-root":
                    outputRoot = RequireValue(args, ref index);
                    break;
                case "--remux":
                    remux = true;
                    break;
                case "--username":
                    username = RequireValue(args, ref index);
                    break;
                case "--password":
                    password = RequireValue(args, ref index);
                    break;
                case "--recordings-folder":
                    recordingsFolder = RequireValue(args, ref index);
                    break;
                case "--file-name-template":
                    fileNameTemplate = RequireValue(args, ref index);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Missing required argument --name");
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("Missing required argument --url");
        }

        return new RunnerOptions
        {
            Name = name,
            Url = url,
            ExpectedFormat = expectedFormat,
            DurationSeconds = durationSeconds,
            StartupTimeoutSeconds = startupTimeoutSeconds,
            OutputRoot = outputRoot,
            RemuxRawAacToM4A = remux,
            Username = username,
            Password = password,
            RecordingsFolder = recordingsFolder,
            FileNameTemplate = fileNameTemplate,
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

internal sealed class RecordingParityResult
{
    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string? ExpectedFormat { get; set; }

    public int RequestedDurationSeconds { get; set; }

    public int StartupTimeoutSeconds { get; set; }

    public bool RemuxRawAacToM4A { get; set; }

    public string RootDirectory { get; set; } = string.Empty;

    public bool Started { get; set; }

    public bool Stopped { get; set; }

    public SnapshotReport? StartSnapshot { get; set; }

    public SnapshotReport? FinalSnapshot { get; set; }

    public OutputReport? Output { get; set; }

    public List<string> LogLines { get; set; } = [];

    public string? Exception { get; set; }

    public bool Pass { get; set; }
}

internal sealed class SnapshotReport
{
    public string? StationName { get; set; }

    public bool Active { get; set; }

    public string? StateLabel { get; set; }

    public string? Format { get; set; }

    public string? OutputPath { get; set; }

    public long BytesWritten { get; set; }

    public int ReconnectCount { get; set; }

    public string? LastError { get; set; }

    public static SnapshotReport? FromSnapshot(RecordingSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        return new SnapshotReport
        {
            StationName = snapshot.StationName,
            Active = snapshot.Active,
            StateLabel = snapshot.StateLabel,
            Format = snapshot.Format?.ToString(),
            OutputPath = snapshot.OutputPath,
            BytesWritten = snapshot.BytesWritten,
            ReconnectCount = snapshot.ReconnectCount,
            LastError = snapshot.LastError,
        };
    }
}

internal sealed class OutputReport
{
    public string Path { get; set; } = string.Empty;

    public bool Exists { get; set; }

    public string Extension { get; set; } = string.Empty;

    public long Length { get; set; }
}
