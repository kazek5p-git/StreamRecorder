using System.Net;
using System.Net.Sockets;
using System.Text;
using StreamRecorder.Core;
using StreamRecorder.Core.Configuration;
using StreamRecorder.Core.Models;

return await ProgramMainAsync(args);

static async Task<int> ProgramMainAsync(string[] args)
{
    var options = ScheduleParityOptions.Parse(args);
    var rootDirectory = options.OutputRoot ?? Path.Combine(Path.GetTempPath(), "StreamRecorderScheduleParity", Guid.NewGuid().ToString("N"));
    var paths = new AppPaths
    {
        RootDirectory = rootDirectory,
        ConfigDirectory = Path.Combine(rootDirectory, AppDefaults.ConfigDirectoryName),
        RecordingsDirectory = Path.Combine(rootDirectory, AppDefaults.DefaultRecordingsFolder),
        ConfigFilePath = Path.Combine(rootDirectory, AppDefaults.ConfigDirectoryName, AppDefaults.ConfigFileName),
        LogFilePath = Path.Combine(rootDirectory, AppDefaults.ConfigDirectoryName, AppDefaults.LogFileName),
    };

    var result = new ScheduleParityResult
    {
        RootDirectory = rootDirectory,
        LeadSeconds = options.LeadSeconds,
    };

    await using var serverA = new LoopbackMp3Server("A");
    await using var serverB = new LoopbackMp3Server("B");

    var stationA = new Station
    {
        Id = Guid.NewGuid(),
        Name = "Schedule Station A",
        Url = serverA.Url,
    };
    var stationB = new Station
    {
        Id = Guid.NewGuid(),
        Name = "Schedule Station B",
        Url = serverB.Url,
    };

    var now = DateTime.Now;
    var startATime = now.AddSeconds(options.LeadSeconds);
    var startBOriginalTime = now.AddSeconds(options.LeadSeconds + 4);
    var startBEditedTime = now.AddSeconds(options.LeadSeconds + 2);
    var stopATime = now.AddSeconds(options.LeadSeconds + 5);
    var stopBTime = now.AddSeconds(options.LeadSeconds + 7);

    var scheduleA = CreateSchedule(stationA.Id, startATime, stopATime);
    var scheduleB = CreateSchedule(stationB.Id, startBOriginalTime, stopBTime);
    var deletedSchedule = CreateSchedule(stationA.Id, now.AddMinutes(5), now.AddMinutes(6));

    result.SeededSchedules =
    [
        ScheduleSeed.FromSchedule(scheduleA, stationA.Name),
        ScheduleSeed.FromSchedule(scheduleB, stationB.Name),
    ];

    try
    {
        using (var seedApp = new StreamRecorderApp("0.2.0-alpha3-scheduler", paths))
        {
            seedApp.SaveSettings(new AppSettings
            {
                RecordingsFolder = AppDefaults.DefaultRecordingsFolder,
                FileNameTemplate = AppDefaults.DefaultFileNameTemplate,
                Language = LanguageCodes.English,
            });

            seedApp.UpsertStation(stationA);
            seedApp.UpsertStation(stationB);

            seedApp.UpsertSchedule(scheduleA);
            seedApp.UpsertSchedule(scheduleB);
            seedApp.UpsertSchedule(deletedSchedule);

            scheduleB.SetStartTime(new TimeSpan(startBEditedTime.Hour, startBEditedTime.Minute, startBEditedTime.Second));
            seedApp.UpsertSchedule(scheduleB);
            seedApp.DeleteSchedule(deletedSchedule.Id);
        }

        using var app = new StreamRecorderApp("0.2.0-alpha3-scheduler", paths);

        var reloadedSchedules = app.GetSchedules();
        result.ReloadedSchedules = reloadedSchedules
            .Select(schedule =>
            {
                var stationName = app.GetStation(schedule.StationId)?.Name ?? "(missing)";
                return ScheduleSeed.FromSchedule(schedule, stationName);
            })
            .ToList();

        result.ScheduleCountPassed = reloadedSchedules.Count == 2;
        result.DeletePassed = reloadedSchedules.All(schedule => schedule.Id != deletedSchedule.Id);

        var reloadedEdited = reloadedSchedules.SingleOrDefault(schedule => schedule.Id == scheduleB.Id);
        result.EditPassed = reloadedEdited is not null
            && reloadedEdited.StartHour == startBEditedTime.Hour
            && reloadedEdited.StartMinute == startBEditedTime.Minute
            && reloadedEdited.StartSecond == startBEditedTime.Second;

        var finalWait = stopBTime.AddSeconds(4);
        while (DateTime.Now < finalWait)
        {
            await Task.Delay(250);
        }

        var snapshotA = app.Recorder.GetSnapshot(stationA.Id);
        var snapshotB = app.Recorder.GetSnapshot(stationB.Id);
        result.SnapshotA = SnapshotReport.FromSnapshot(snapshotA);
        result.SnapshotB = SnapshotReport.FromSnapshot(snapshotB);
        result.OutputA = OutputReport.FromPath(snapshotA?.OutputPath);
        result.OutputB = OutputReport.FromPath(snapshotB?.OutputPath);
        result.ServerConnectionsA = serverA.ConnectionCount;
        result.ServerConnectionsB = serverB.ConnectionCount;
        result.LogLines = app.Logs.Entries.Select(static entry => entry.FormatLine()).ToList();

        result.StartStopPassed =
            HasLog(result.LogLines, $"Schedule started recording: {stationA.Name}") &&
            HasLog(result.LogLines, $"Schedule started recording: {stationB.Name}") &&
            HasLog(result.LogLines, $"Schedule stopped recording: {stationA.Name}") &&
            HasLog(result.LogLines, $"Schedule stopped recording: {stationB.Name}") &&
            SnapshotStopped(snapshotA) &&
            SnapshotStopped(snapshotB) &&
            result.OutputA is { Exists: true, Length: > 0 } &&
            result.OutputB is { Exists: true, Length: > 0 } &&
            serverA.ConnectionCount >= 1 &&
            serverB.ConnectionCount >= 1;

        result.Pass = result.ScheduleCountPassed && result.EditPassed && result.DeletePassed && result.StartStopPassed;
    }
    catch (Exception ex)
    {
        result.Exception = ex.ToString();
        result.Pass = false;
    }

    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
    return result.Pass ? 0 : 1;
}

static ScheduleEntry CreateSchedule(Guid stationId, DateTime start, DateTime end)
{
    return new ScheduleEntry
    {
        Id = Guid.NewGuid(),
        StationId = stationId,
        Enabled = true,
        DayOfWeek = start.DayOfWeek,
        StartHour = start.Hour,
        StartMinute = start.Minute,
        StartSecond = start.Second,
        EndHour = end.Hour,
        EndMinute = end.Minute,
        EndSecond = end.Second,
    };
}

static bool HasLog(IEnumerable<string> lines, string fragment)
{
    return lines.Any(line => line.IndexOf(fragment, StringComparison.Ordinal) >= 0);
}

static bool SnapshotStopped(RecordingSnapshot? snapshot)
{
    return snapshot is not null
        && !snapshot.Active
        && string.Equals(snapshot.StateLabel, "Stopped", StringComparison.OrdinalIgnoreCase)
        && snapshot.BytesWritten > 0;
}

internal sealed class ScheduleParityOptions
{
    public int LeadSeconds { get; set; } = 4;

    public string? OutputRoot { get; set; }

    public static ScheduleParityOptions Parse(IReadOnlyList<string> args)
    {
        var leadSeconds = 4;
        string? outputRoot = null;

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--lead-seconds":
                    leadSeconds = int.Parse(RequireValue(args, ref index));
                    break;
                case "--output-root":
                    outputRoot = RequireValue(args, ref index);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[index]}");
            }
        }

        return new ScheduleParityOptions
        {
            LeadSeconds = leadSeconds,
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

internal sealed class ScheduleParityResult
{
    public string RootDirectory { get; set; } = string.Empty;

    public int LeadSeconds { get; set; }

    public List<ScheduleSeed> SeededSchedules { get; set; } = [];

    public List<ScheduleSeed> ReloadedSchedules { get; set; } = [];

    public bool ScheduleCountPassed { get; set; }

    public bool EditPassed { get; set; }

    public bool DeletePassed { get; set; }

    public bool StartStopPassed { get; set; }

    public SnapshotReport? SnapshotA { get; set; }

    public SnapshotReport? SnapshotB { get; set; }

    public OutputReport? OutputA { get; set; }

    public OutputReport? OutputB { get; set; }

    public int ServerConnectionsA { get; set; }

    public int ServerConnectionsB { get; set; }

    public List<string> LogLines { get; set; } = [];

    public string? Exception { get; set; }

    public bool Pass { get; set; }
}

internal sealed class ScheduleSeed
{
    public Guid Id { get; set; }

    public string StationName { get; set; } = string.Empty;

    public string Day { get; set; } = string.Empty;

    public string StartTime { get; set; } = string.Empty;

    public string EndTime { get; set; } = string.Empty;

    public static ScheduleSeed FromSchedule(ScheduleEntry schedule, string stationName)
    {
        return new ScheduleSeed
        {
            Id = schedule.Id,
            StationName = stationName,
            Day = schedule.DayOfWeek.ToString(),
            StartTime = $"{schedule.StartHour:00}:{schedule.StartMinute:00}:{schedule.StartSecond:00}",
            EndTime = $"{schedule.EndHour:00}:{schedule.EndMinute:00}:{schedule.EndSecond:00}",
        };
    }
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

    public static OutputReport? FromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var exists = File.Exists(path);
        var outputPath = path!;
        return new OutputReport
        {
            Path = outputPath,
            Exists = exists,
            Extension = System.IO.Path.GetExtension(outputPath) ?? string.Empty,
            Length = exists ? new FileInfo(outputPath).Length : 0,
        };
    }
}

internal sealed class LoopbackMp3Server : IAsyncDisposable
{
    private readonly string name;
    private readonly CancellationTokenSource cancellation = new();
    private readonly TcpListener listener;
    private readonly Task acceptLoopTask;
    private int connectionCount;

    public LoopbackMp3Server(string name)
    {
        this.name = name;
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Url = $"http://127.0.0.1:{Port}/stream.mp3";
        acceptLoopTask = Task.Run(() => AcceptLoopAsync(cancellation.Token));
    }

    public int Port { get; }

    public string Url { get; }

    public int ConnectionCount => Volatile.Read(ref connectionCount);

    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel();
        listener.Stop();
        try
        {
            await acceptLoopTask;
        }
        catch
        {
        }

        cancellation.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        var chunk = new byte[4096];
        for (var index = 0; index < chunk.Length; index++)
        {
            chunk[index] = (byte)(0x20 + (index % 90));
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await listener.AcceptTcpClientAsync();
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            if (client is null)
            {
                continue;
            }

            _ = Task.Run(() => ServeClientAsync(client, chunk, cancellationToken), cancellationToken);
        }
    }

    private async Task ServeClientAsync(TcpClient client, byte[] chunk, CancellationToken cancellationToken)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            Interlocked.Increment(ref connectionCount);

            var requestBuffer = new byte[2048];
            try
            {
                _ = await stream.ReadAsync(requestBuffer, 0, requestBuffer.Length, cancellationToken);
            }
            catch
            {
            }

            var headers = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: audio/mpeg\r\n" +
                "X-Stream-Name: " + name + "\r\n" +
                "Cache-Control: no-cache\r\n" +
                "Connection: close\r\n\r\n");
            await stream.WriteAsync(headers, 0, headers.Length, cancellationToken);

            byte[] id3 = [0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x3F];
            await stream.WriteAsync(id3, 0, id3.Length, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await stream.WriteAsync(chunk, 0, chunk.Length, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    await Task.Delay(100, cancellationToken);
                }
                catch
                {
                    break;
                }
            }
        }
    }
}
