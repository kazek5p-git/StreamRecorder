using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Microsoft.Win32;
using StreamRecorder.Core;
using StreamRecorder.Core.Configuration;
using StreamRecorder.Core.Models;

return await ProgramMainAsync(args);

static async Task<int> ProgramMainAsync(string[] args)
{
    var options = SettingsParityOptions.Parse(args);
    var rootDirectory = options.OutputRoot ?? Path.Combine(Path.GetTempPath(), "StreamRecorderSettingsParity", Guid.NewGuid().ToString("N"));
    var paths = new AppPaths
    {
        RootDirectory = rootDirectory,
        ConfigDirectory = Path.Combine(rootDirectory, AppDefaults.ConfigDirectoryName),
        RecordingsDirectory = Path.Combine(rootDirectory, AppDefaults.DefaultRecordingsFolder),
        ConfigFilePath = Path.Combine(rootDirectory, AppDefaults.ConfigDirectoryName, AppDefaults.ConfigFileName),
        LogFilePath = Path.Combine(rootDirectory, AppDefaults.ConfigDirectoryName, AppDefaults.LogFileName),
    };

    var expectedSettings = new AppSettings
    {
        LaunchOnStartup = true,
        AlwaysOnTop = true,
        MinimizeToTray = false,
        ConfirmOnExit = false,
        RestartOnCrash = true,
        PreventSleep = true,
        StartMinimized = true,
        RemuxRawAacToM4A = false,
        RecordingsFolder = "Custom recordings",
        FileNameTemplate = "%t_custom_%h-%m-%s",
        Language = LanguageCodes.English,
    };

    var result = new SettingsParityResult
    {
        RootDirectory = rootDirectory,
        RequestedDurationSeconds = options.DurationSeconds,
        StartupTimeoutSeconds = options.StartupTimeoutSeconds,
    };

    try
    {
        await using var server = new LoopbackMp3Server();

        using (var app = new StreamRecorderApp("0.2.0-alpha2-settings", paths))
        {
            app.SaveSettings(CloneSettings(expectedSettings));
        }

        using var reloaded = new StreamRecorderApp("0.2.0-alpha2-settings", paths);
        var loadedSettings = reloaded.GetSettings();
        result.SettingsPersisted = SettingsEqual(expectedSettings, loadedSettings);
        result.LoadedSettings = SettingsReport.FromSettings(loadedSettings);

        var station = new Station
        {
            Id = Guid.NewGuid(),
            Name = "Settings custom output",
            Url = server.Url,
        };
        reloaded.UpsertStation(station);

        await reloaded.StartRecordingAsync(station.Id);
        result.Started = await WaitForRecordingStartAsync(reloaded, station.Id, TimeSpan.FromSeconds(options.StartupTimeoutSeconds));
        result.StartSnapshot = SnapshotReport.FromSnapshot(reloaded.Recorder.GetSnapshot(station.Id));

        if (result.Started)
        {
            await Task.Delay(TimeSpan.FromSeconds(options.DurationSeconds));
        }

        reloaded.StopRecording(station.Id);
        result.Stopped = await WaitForRecordingStopAsync(reloaded, station.Id, TimeSpan.FromSeconds(20));
        result.FinalSnapshot = SnapshotReport.FromSnapshot(reloaded.Recorder.GetSnapshot(station.Id));

        if (!string.IsNullOrWhiteSpace(result.FinalSnapshot?.OutputPath))
        {
            result.Output = OutputReport.FromPath(result.FinalSnapshot.OutputPath!);
        }

        result.RecordingOutputPassed = result.Started
            && result.Stopped
            && result.Output is { Exists: true, Length: > 0 }
            && result.Output.Path.Contains($"{Path.DirectorySeparatorChar}{expectedSettings.RecordingsFolder}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            && string.Equals(result.FinalSnapshot?.Format, nameof(StreamFormat.Mp3), StringComparison.Ordinal)
            && Path.GetFileNameWithoutExtension(result.Output.Path).StartsWith("Settings custom output_custom_", StringComparison.Ordinal);

        result.ServerConnections = server.ConnectionCount;
        result.LogLines = reloaded.Logs.Entries.Select(static entry => entry.FormatLine()).ToList();
        result.StartupRegistrationPassed = TestStartupRegistration(rootDirectory, out var startupDetails);
        result.StartupDetails = startupDetails;
        result.Pass = result.SettingsPersisted && result.RecordingOutputPassed && result.StartupRegistrationPassed;
    }
    catch (Exception ex)
    {
        result.Exception = ex.ToString();
        result.Pass = false;
    }

    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
    return result.Pass ? 0 : 1;
}

static async Task<bool> WaitForRecordingStartAsync(StreamRecorderApp app, Guid stationId, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        var snapshot = app.Recorder.GetSnapshot(stationId);
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

static async Task<bool> WaitForRecordingStopAsync(StreamRecorderApp app, Guid stationId, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        var snapshot = app.Recorder.GetSnapshot(stationId);
        if (snapshot is not null && !snapshot.Active && string.Equals(snapshot.StateLabel, "Stopped", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        await Task.Delay(250);
    }

    return false;
}

static bool SettingsEqual(AppSettings expected, AppSettings actual)
{
    return expected.LaunchOnStartup == actual.LaunchOnStartup
        && expected.AlwaysOnTop == actual.AlwaysOnTop
        && expected.MinimizeToTray == actual.MinimizeToTray
        && expected.ConfirmOnExit == actual.ConfirmOnExit
        && expected.RestartOnCrash == actual.RestartOnCrash
        && expected.PreventSleep == actual.PreventSleep
        && expected.StartMinimized == actual.StartMinimized
        && expected.RemuxRawAacToM4A == actual.RemuxRawAacToM4A
        && string.Equals(expected.RecordingsFolder, actual.RecordingsFolder, StringComparison.Ordinal)
        && string.Equals(expected.FileNameTemplate, actual.FileNameTemplate, StringComparison.Ordinal)
        && expected.Language == actual.Language;
}

static AppSettings CloneSettings(AppSettings source)
{
    return new AppSettings
    {
        LaunchOnStartup = source.LaunchOnStartup,
        AlwaysOnTop = source.AlwaysOnTop,
        MinimizeToTray = source.MinimizeToTray,
        ConfirmOnExit = source.ConfirmOnExit,
        RestartOnCrash = source.RestartOnCrash,
        PreventSleep = source.PreventSleep,
        StartMinimized = source.StartMinimized,
        RemuxRawAacToM4A = source.RemuxRawAacToM4A,
        RecordingsFolder = source.RecordingsFolder,
        FileNameTemplate = source.FileNameTemplate,
        Language = source.Language,
    };
}

static bool TestStartupRegistration(string rootDirectory, out StartupDetails details)
{
    const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string valueName = "StreamRecorder";
    using var currentUser = Registry.CurrentUser;
    using var runKey = currentUser.CreateSubKey(runKeyPath)
        ?? throw new InvalidOperationException("Could not open the Windows Run registry key.");

    var backup = runKey.GetValue(valueName) as string;
    var hadBackup = backup is not null;
    var executablePath = Path.Combine(rootDirectory, "StreamRecorder.exe");

    try
    {
        var assemblyPath = Path.Combine(AppContext.BaseDirectory, "StreamRecorder.dll");
        var assembly = Assembly.LoadFrom(assemblyPath);
        var type = assembly.GetType("StreamRecorder.WinForms.Services.WindowsStartupRegistration", throwOnError: true)
            ?? throw new InvalidOperationException("Could not resolve WindowsStartupRegistration.");
        var instance = Activator.CreateInstance(type, nonPublic: true)
            ?? throw new InvalidOperationException("Could not instantiate WindowsStartupRegistration.");
        var applyMethod = type.GetMethod("Apply", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not resolve WindowsStartupRegistration.Apply.");

        applyMethod.Invoke(instance, [true, executablePath]);
        var enabledValue = runKey.GetValue(valueName) as string;
        var expectedValue = $"\"{executablePath}\"";
        var enabledPass = string.Equals(enabledValue, expectedValue, StringComparison.Ordinal);

        applyMethod.Invoke(instance, [false, executablePath]);
        var removedPass = runKey.GetValue(valueName) is null;

        details = new StartupDetails
        {
            EnabledValue = enabledValue,
            ExpectedValue = expectedValue,
            EnabledPass = enabledPass,
            RemovedPass = removedPass,
        };

        return enabledPass && removedPass;
    }
    finally
    {
        if (hadBackup)
        {
            runKey.SetValue(valueName, backup!, RegistryValueKind.String);
        }
        else
        {
            runKey.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }
}

internal sealed class SettingsParityOptions
{
    public int DurationSeconds { get; set; } = 8;

    public int StartupTimeoutSeconds { get; set; } = 20;

    public string? OutputRoot { get; set; }

    public static SettingsParityOptions Parse(IReadOnlyList<string> args)
    {
        var durationSeconds = 8;
        var startupTimeoutSeconds = 20;
        string? outputRoot = null;

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--duration-seconds":
                    durationSeconds = int.Parse(RequireValue(args, ref index));
                    break;
                case "--startup-timeout-seconds":
                    startupTimeoutSeconds = int.Parse(RequireValue(args, ref index));
                    break;
                case "--output-root":
                    outputRoot = RequireValue(args, ref index);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[index]}");
            }
        }

        return new SettingsParityOptions
        {
            DurationSeconds = durationSeconds,
            StartupTimeoutSeconds = startupTimeoutSeconds,
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

internal sealed class SettingsParityResult
{
    public string RootDirectory { get; set; } = string.Empty;

    public int RequestedDurationSeconds { get; set; }

    public int StartupTimeoutSeconds { get; set; }

    public bool SettingsPersisted { get; set; }

    public SettingsReport? LoadedSettings { get; set; }

    public bool Started { get; set; }

    public bool Stopped { get; set; }

    public SnapshotReport? StartSnapshot { get; set; }

    public SnapshotReport? FinalSnapshot { get; set; }

    public OutputReport? Output { get; set; }

    public bool RecordingOutputPassed { get; set; }

    public int ServerConnections { get; set; }

    public bool StartupRegistrationPassed { get; set; }

    public StartupDetails? StartupDetails { get; set; }

    public List<string> LogLines { get; set; } = [];

    public string? Exception { get; set; }

    public bool Pass { get; set; }
}

internal sealed class SettingsReport
{
    public bool LaunchOnStartup { get; set; }

    public bool AlwaysOnTop { get; set; }

    public bool MinimizeToTray { get; set; }

    public bool ConfirmOnExit { get; set; }

    public bool RestartOnCrash { get; set; }

    public bool PreventSleep { get; set; }

    public bool StartMinimized { get; set; }

    public bool RemuxRawAacToM4A { get; set; }

    public string RecordingsFolder { get; set; } = string.Empty;

    public string FileNameTemplate { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public static SettingsReport FromSettings(AppSettings settings)
    {
        return new SettingsReport
        {
            LaunchOnStartup = settings.LaunchOnStartup,
            AlwaysOnTop = settings.AlwaysOnTop,
            MinimizeToTray = settings.MinimizeToTray,
            ConfirmOnExit = settings.ConfirmOnExit,
            RestartOnCrash = settings.RestartOnCrash,
            PreventSleep = settings.PreventSleep,
            StartMinimized = settings.StartMinimized,
            RemuxRawAacToM4A = settings.RemuxRawAacToM4A,
            RecordingsFolder = settings.RecordingsFolder,
            FileNameTemplate = settings.FileNameTemplate,
            Language = settings.Language,
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
        return new OutputReport
        {
            Path = path,
            Exists = exists,
            Extension = System.IO.Path.GetExtension(path),
            Length = exists ? new FileInfo(path).Length : 0,
        };
    }
}

internal sealed class StartupDetails
{
    public string? EnabledValue { get; set; }

    public string ExpectedValue { get; set; } = string.Empty;

    public bool EnabledPass { get; set; }

    public bool RemovedPass { get; set; }
}

internal sealed class LoopbackMp3Server : IAsyncDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly TcpListener listener;
    private readonly Task acceptLoopTask;
    private int connectionCount;

    public LoopbackMp3Server()
    {
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
                client = await listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
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
        await using (var stream = client.GetStream())
        {
            Interlocked.Increment(ref connectionCount);

            var requestBuffer = new byte[2048];
            try
            {
                _ = await stream.ReadAsync(requestBuffer, cancellationToken);
            }
            catch
            {
            }

            var headers = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: audio/mpeg\r\n" +
                "Cache-Control: no-cache\r\n" +
                "Connection: close\r\n\r\n");
            await stream.WriteAsync(headers, cancellationToken);

            byte[] id3 = [0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x3F];
            await stream.WriteAsync(id3, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await stream.WriteAsync(chunk, cancellationToken);
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
