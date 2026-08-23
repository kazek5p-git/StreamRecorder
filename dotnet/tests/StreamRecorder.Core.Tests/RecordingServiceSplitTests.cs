using System.Net;
using System.Net.Sockets;
using System.Text;
using StreamRecorder.Core.Configuration;
using StreamRecorder.Core.Logging;
using StreamRecorder.Core.Models;
using StreamRecorder.Core.Recording;

namespace StreamRecorder.Core.Tests;

public sealed class RecordingServiceSplitTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"sr_core_recording_{Guid.NewGuid():N}");

    [Fact]
    public async Task RecordingService_SplitsOutputFiles_WhenSplitIntervalIsEnabled()
    {
        var paths = AppPaths.Discover(
            Path.Combine(tempRoot, "streamrecorder.exe"),
            Path.Combine(tempRoot, AppDefaults.DefaultRecordingsFolderName));
        paths.EnsureDirectories();

        using var server = new StreamingHttpServer();
        using var recorder = new RecordingService("tests");
        var logs = new LogBus(paths.LogFilePath);
        var station = new Station
        {
            Id = Guid.NewGuid(),
            Name = "Split Test",
            Url = server.Url,
        };
        var settings = new AppSettings
        {
            RecordingsFolder = AppDefaults.DefaultRecordingsFolderName,
            FileNameTemplate = "%t_%h-%m-%s",
            Language = LanguageCodes.English,
            RemuxRawAacToM4A = false,
            SplitRecordingsEnabled = true,
            SplitSeconds = 1,
        };

        await recorder.StartAsync(station, settings, paths, logs);
        Assert.True(await WaitUntilAsync(() => recorder.GetSnapshot(station.Id)?.BytesWritten > 0, TimeSpan.FromSeconds(5)));

        await Task.Delay(TimeSpan.FromSeconds(3.5));
        recorder.Stop(station.Id);
        Assert.True(await WaitUntilAsync(() => !recorder.IsRecording(station.Id), TimeSpan.FromSeconds(10)));

        var files = Directory.GetFiles(paths.RecordingsDirectory, "*.mp3");
        Assert.True(files.Length >= 2, $"Expected at least 2 split files, got {files.Length}.");
        Assert.All(files, path => Assert.True(new FileInfo(path).Length > 0, path));
    }

    [Fact]
    public async Task RecordingService_ReconnectsAfterStreamDisconnects()
    {
        var paths = AppPaths.Discover(
            Path.Combine(tempRoot, "streamrecorder.exe"),
            Path.Combine(tempRoot, AppDefaults.DefaultRecordingsFolderName));
        paths.EnsureDirectories();

        using var server = new StreamingHttpServer(closeAfterWrites: 3);
        using var recorder = new RecordingService("tests");
        var logs = new LogBus(paths.LogFilePath);
        var station = new Station
        {
            Id = Guid.NewGuid(),
            Name = "Reconnect Test",
            Url = server.Url,
        };

        await recorder.StartAsync(station, CreateDefaultSettings(), paths, logs);
        Assert.True(await WaitUntilAsync(() => recorder.GetSnapshot(station.Id)?.BytesWritten > 0, TimeSpan.FromSeconds(5)));
        Assert.True(await WaitUntilAsync(() => recorder.GetSnapshot(station.Id)?.ReconnectCount > 0, TimeSpan.FromSeconds(8)));
        Assert.True(await WaitUntilAsync(() => server.ConnectionCount >= 2, TimeSpan.FromSeconds(8)));

        recorder.Stop(station.Id);
        Assert.True(await WaitUntilAsync(() => !recorder.IsRecording(station.Id), TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task RecordingService_SavesIcyTitlesWithoutWritingMetadataIntoAudio()
    {
        var paths = AppPaths.Discover(
            Path.Combine(tempRoot, "streamrecorder.exe"),
            Path.Combine(tempRoot, AppDefaults.DefaultRecordingsFolderName));
        paths.EnsureDirectories();

        using var server = new IcyMetadataStreamingServer();
        using var recorder = new RecordingService("tests");
        var logs = new LogBus(paths.LogFilePath);
        var station = new Station
        {
            Id = Guid.NewGuid(),
            Name = "ICY Title Test",
            Url = server.Url,
            SaveStreamTitles = true,
        };

        await recorder.StartAsync(station, CreateDefaultSettings(), paths, logs);
        Assert.True(
            await WaitUntilAsync(
                () => Directory.GetFiles(paths.RecordingsDirectory, "*.txt").Length > 0,
                TimeSpan.FromSeconds(8)));

        recorder.Stop(station.Id);
        Assert.True(await WaitUntilAsync(() => !recorder.IsRecording(station.Id), TimeSpan.FromSeconds(10)));

        var titleFile = Assert.Single(Directory.GetFiles(paths.RecordingsDirectory, "*.txt"));
        var titleContents = File.ReadAllText(titleFile);
        Assert.Contains("Test Song", titleContents, StringComparison.Ordinal);

        var audioFile = Assert.Single(Directory.GetFiles(paths.RecordingsDirectory, "*.mp3"));
        var audioContents = Encoding.ASCII.GetString(File.ReadAllBytes(audioFile));
        Assert.DoesNotContain("StreamTitle=", audioContents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordingService_SavesCueSheetWithoutTextFile_WhenCueSheetsAreEnabled()
    {
        var paths = AppPaths.Discover(
            Path.Combine(tempRoot, "streamrecorder.exe"),
            Path.Combine(tempRoot, AppDefaults.DefaultRecordingsFolderName));
        paths.EnsureDirectories();

        using var server = new IcyMetadataStreamingServer();
        using var recorder = new RecordingService("tests");
        var logs = new LogBus(paths.LogFilePath);
        var station = new Station
        {
            Id = Guid.NewGuid(),
            Name = "CUE Test",
            Url = server.Url,
        };
        var settings = CreateDefaultSettings();
        settings.CreateCueSheets = true;

        await recorder.StartAsync(station, settings, paths, logs);
        Assert.True(await WaitUntilAsync(() => recorder.GetSnapshot(station.Id)?.BytesWritten > 0, TimeSpan.FromSeconds(8)));

        recorder.Stop(station.Id);
        Assert.True(await WaitUntilAsync(() => !recorder.IsRecording(station.Id), TimeSpan.FromSeconds(10)));

        var cueFile = Assert.Single(Directory.GetFiles(paths.RecordingsDirectory, "*.cue"));
        var cueContents = File.ReadAllText(cueFile);
        Assert.Contains("FILE \"", cueContents, StringComparison.Ordinal);
        Assert.Contains("TITLE \"Test Song\"", cueContents, StringComparison.Ordinal);
        Assert.Contains("INDEX 01 00:00:", cueContents, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(paths.RecordingsDirectory, "*.txt"));
    }

    [Fact]
    public async Task RecordingService_StopCompletesWhenStreamReadIsStalled()
    {
        var paths = AppPaths.Discover(
            Path.Combine(tempRoot, "streamrecorder.exe"),
            Path.Combine(tempRoot, AppDefaults.DefaultRecordingsFolderName));
        paths.EnsureDirectories();

        using var server = new StreamingHttpServer(stallAfterWrites: 2);
        using var recorder = new RecordingService("tests");
        var logs = new LogBus(paths.LogFilePath);
        var station = new Station
        {
            Id = Guid.NewGuid(),
            Name = "Stall Stop Test",
            Url = server.Url,
        };

        await recorder.StartAsync(station, CreateDefaultSettings(), paths, logs);
        Assert.True(await WaitUntilAsync(() => recorder.GetSnapshot(station.Id)?.BytesWritten > 0, TimeSpan.FromSeconds(5)));

        recorder.Stop(station.Id);

        Assert.True(await WaitUntilAsync(() => !recorder.IsRecording(station.Id), TimeSpan.FromSeconds(5)));
        Assert.Equal("Stopped", recorder.GetSnapshot(station.Id)?.StateLabel);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.Now + timeout;
        while (DateTimeOffset.Now < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private static AppSettings CreateDefaultSettings()
    {
        return new AppSettings
        {
            RecordingsFolder = AppDefaults.DefaultRecordingsFolderName,
            FileNameTemplate = "%t_%h-%m-%s",
            Language = LanguageCodes.English,
            RemuxRawAacToM4A = false,
        };
    }

    private sealed class StreamingHttpServer : IDisposable
    {
        private readonly TcpListener listener;
        private readonly CancellationTokenSource cancellation = new();
        private readonly Task acceptLoop;
        private readonly int? closeAfterWrites;
        private readonly int? stallAfterWrites;
        private int connectionCount;

        public StreamingHttpServer(int? closeAfterWrites = null, int? stallAfterWrites = null)
        {
            this.closeAfterWrites = closeAfterWrites;
            this.stallAfterWrites = stallAfterWrites;
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            Url = $"http://127.0.0.1:{endpoint.Port}/stream.mp3";
            acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public string Url { get; }

        public int ConnectionCount => Volatile.Read(ref connectionCount);

        public void Dispose()
        {
            cancellation.Cancel();
            listener.Stop();
            try
            {
                acceptLoop.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }
            cancellation.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            while (!cancellation.IsCancellationRequested)
            {
                TcpClient? client = null;
                try
                {
                    client = await listener.AcceptTcpClientAsync();
                    Interlocked.Increment(ref connectionCount);
                    _ = Task.Run(() => ServeClientAsync(client, cancellation.Token));
                }
                catch when (cancellation.IsCancellationRequested)
                {
                    client?.Dispose();
                    return;
                }
                catch
                {
                    client?.Dispose();
                }
            }
        }

        private async Task ServeClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    var responseHeader = System.Text.Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Type: audio/mpeg\r\nCache-Control: no-cache\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(responseHeader, 0, responseHeader.Length, cancellationToken);

                    var payload = new byte[2048];
                    payload[0] = (byte)'I';
                    payload[1] = (byte)'D';
                    payload[2] = (byte)'3';
                    for (var index = 3; index < payload.Length; index++)
                    {
                        payload[index] = (byte)(index % 251);
                    }

                    var writes = 0;
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        if (stallAfterWrites is { } stallLimit && writes >= stallLimit)
                        {
                            await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
                            return;
                        }

                        if (closeAfterWrites is { } closeLimit && writes >= closeLimit)
                        {
                            return;
                        }

                        await stream.WriteAsync(payload, 0, payload.Length, cancellationToken);
                        await stream.FlushAsync(cancellationToken);
                        writes += 1;
                        await Task.Delay(50, cancellationToken);
                    }
                }
            }
            catch when (cancellationToken.IsCancellationRequested)
            {
            }
            catch
            {
            }
        }
    }

    private sealed class IcyMetadataStreamingServer : IDisposable
    {
        private readonly TcpListener listener;
        private readonly CancellationTokenSource cancellation = new();
        private readonly Task acceptLoop;

        public IcyMetadataStreamingServer()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            Url = $"http://127.0.0.1:{endpoint.Port}/stream";
            acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public string Url { get; }

        public void Dispose()
        {
            cancellation.Cancel();
            listener.Stop();
            try
            {
                acceptLoop.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }

            cancellation.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            while (!cancellation.IsCancellationRequested)
            {
                TcpClient? client = null;
                try
                {
                    client = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => ServeClientAsync(client, cancellation.Token));
                }
                catch when (cancellation.IsCancellationRequested)
                {
                    client?.Dispose();
                    return;
                }
                catch
                {
                    client?.Dispose();
                }
            }
        }

        private static async Task ServeClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    var header = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Type: audio/mpeg\r\nicy-metaint: 32\r\nCache-Control: no-cache\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header, 0, header.Length, cancellationToken);

                    var audio = new byte[32];
                    audio[0] = 0xFF;
                    audio[1] = 0xFB;
                    for (var index = 2; index < audio.Length; index++)
                    {
                        audio[index] = 0x55;
                    }

                    var metadata = new byte[64];
                    var metadataText = Encoding.UTF8.GetBytes("StreamTitle='Test Song';");
                    Buffer.BlockCopy(metadataText, 0, metadata, 0, metadataText.Length);

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        await stream.WriteAsync(audio, 0, audio.Length, cancellationToken);
                        await stream.WriteAsync(new[] { (byte)4 }, 0, 1, cancellationToken);
                        await stream.WriteAsync(metadata, 0, metadata.Length, cancellationToken);
                        await stream.FlushAsync(cancellationToken);
                        await Task.Delay(25, cancellationToken);
                    }
                }
            }
            catch when (cancellationToken.IsCancellationRequested)
            {
            }
            catch
            {
            }
        }
    }
}
