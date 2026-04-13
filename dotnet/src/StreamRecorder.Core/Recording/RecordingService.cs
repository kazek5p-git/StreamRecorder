using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using StreamRecorder.Core.Compatibility;
using StreamRecorder.Core.Configuration;
using StreamRecorder.Core.Localization;
using StreamRecorder.Core.Logging;
using StreamRecorder.Core.Models;
using StreamRecorder.Core.Naming;
using StreamRecorder.Core.Probing;
using StreamRecorder.Core.Tools;

namespace StreamRecorder.Core.Recording;

public sealed class RecordingService : IDisposable
{
    private const int InitialProbeBytes = 16 * 1024;
    private const int SegmentHistoryLimit = 2048;

    private readonly string currentVersion;
    private readonly HttpClient httpClient;
    private readonly ConcurrentDictionary<Guid, RecordingSession> sessions = new();
    private readonly ConcurrentDictionary<Guid, RecordingSnapshot> snapshots = new();

    public RecordingService(string currentVersion)
    {
        this.currentVersion = currentVersion;
        httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"StreamRecorder/{currentVersion}");
        httpClient.DefaultRequestHeaders.Add("Icy-MetaData", "0");
        httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
    }

    public event Action? SnapshotsChanged;

    public bool IsRecording(Guid stationId)
    {
        return sessions.ContainsKey(stationId);
    }

    public IReadOnlyDictionary<Guid, RecordingSnapshot> GetSnapshots()
    {
        return snapshots.ToDictionary(static pair => pair.Key, static pair => CloneSnapshot(pair.Value));
    }

    public RecordingSnapshot? GetSnapshot(Guid stationId)
    {
        return snapshots.TryGetValue(stationId, out var snapshot) ? CloneSnapshot(snapshot) : null;
    }

    public Task StartAsync(
        Station station,
        AppSettings settings,
        AppPaths paths,
        LogBus logs,
        CancellationToken cancellationToken = default)
    {
        if (sessions.ContainsKey(station.Id))
        {
            return Task.CompletedTask;
        }

        var localizer = AppLocalizer.For(settings.Language);
        var snapshot = RecordingSnapshot.CreateInitial(station);
        snapshot.Active = true;
        snapshot.StateLabel = "Connecting";
        snapshot.StartedAt = DateTimeOffset.Now;
        snapshots[station.Id] = snapshot;
        RaiseSnapshotsChanged();

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var task = Task.Run(async () =>
        {
            try
            {
                await RecordStationAsync(station, settings, paths, logs, cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logs.Push($"{station.Name}: {ex.Message}");
                UpdateSnapshot(station.Id, value =>
                {
                    value.Active = false;
                    value.StateLabel = localizer.ErrorPrefix + ex.Message;
                    value.LastError = ex.Message;
                });
            }
            finally
            {
                sessions.TryRemove(station.Id, out _);
                RaiseSnapshotsChanged();
            }
        }, CancellationToken.None);

        sessions[station.Id] = new RecordingSession(cts, task);
        return Task.CompletedTask;
    }

    public void Stop(Guid stationId)
    {
        if (sessions.TryGetValue(stationId, out var session))
        {
            session.Cancellation.Cancel();
        }

        UpdateSnapshot(stationId, value => value.StateLabel = "Stopping");
    }

    public void StopAll()
    {
        foreach (var stationId in sessions.Keys.ToArray())
        {
            Stop(stationId);
        }
    }

    private async Task RecordStationAsync(
        Station station,
        AppSettings settings,
        AppPaths paths,
        LogBus logs,
        CancellationToken cancellationToken)
    {
        if (station.Url.StartsWith("mms://", StringComparison.OrdinalIgnoreCase)
            || station.Url.StartsWith("mmsh://", StringComparison.OrdinalIgnoreCase))
        {
            await RecordMmshLoopAsync(station, settings, paths, logs, cancellationToken);
        }
        else if (station.Url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            await RecordHlsLoopAsync(station, settings, paths, logs, cancellationToken);
        }
        else
        {
            await RecordHttpLoopAsync(station, settings, paths, logs, cancellationToken);
        }
    }

    private async Task RecordMmshLoopAsync(
        Station station,
        AppSettings settings,
        AppPaths paths,
        LogBus logs,
        CancellationToken cancellationToken)
    {
        OutputSession? output = null;
        var localizer = AppLocalizer.For(settings.Language);
        var requestContext = 1;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UpdateSnapshot(station.Id, value =>
                {
                    value.StateLabel = value.OutputPath is not null ? "Reconnecting" : "Connecting";
                });

                HttpResponseMessage? response = null;
                try
                {
                    using var request = BuildMmshRequest(station, requestContext++);
                    response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logs.Push(localizer.ConnectionFailed(station.Name, ex.Message));
                    NoteReconnect(station.Id, "Waiting for reconnect");
                    await WaitBeforeRetryAsync(cancellationToken);
                    continue;
                }

                using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var contentType = response.Content.Headers.ContentType?.MediaType;
                var initialBytes = await ReadInitialMmshBytesAsync(responseStream, cancellationToken);
                if (initialBytes.Length == 0)
                {
                    response.Dispose();
                    logs.Push(localizer.StreamProducedNoDataRetrying(station.Name));
                    NoteReconnect(station.Id, "Waiting for reconnect");
                    await WaitBeforeRetryAsync(cancellationToken);
                    continue;
                }

                if (output is null)
                {
                    var probe = StreamProbeService.ProbeStream(station.Url, contentType, initialBytes);
                    LogUnknownFormatDetails(logs, localizer, station.Name, station.Url, probe, initialBytes);
                    var outputPath = FileNameTemplate.BuildOutputPath(paths, settings, station, probe.Extension, DateTimeOffset.Now);
                    var file = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read, 81920, useAsync: true);
                    await file.WriteAsync(initialBytes, cancellationToken);
                    output = new OutputSession(file, outputPath, probe.Format);
                    MarkOutputStarted(station, probe.Format, outputPath);
                    IncrementBytesWritten(station.Id, initialBytes.LongLength);
                    logs.Push(localizer.RecordingStarted(station.Name, outputPath, probe.Format.GetDisplayName(settings.Language)));
                }
                else
                {
                    await output.File.WriteAsync(initialBytes, cancellationToken);
                    IncrementBytesWritten(station.Id, initialBytes.LongLength);
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var chunk = await MmshStreamReader.ReadChunkAsync(responseStream, cancellationToken);
                        if (chunk is null)
                        {
                            logs.Push(localizer.ConnectionEndedRetrying(station.Name));
                            NoteReconnect(station.Id, "Waiting for reconnect");
                            break;
                        }

                        if (chunk.Data.Length == 0)
                        {
                            continue;
                        }

                        await output.File.WriteAsync(chunk.Data, cancellationToken);
                        IncrementBytesWritten(station.Id, chunk.Data.LongLength);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        logs.Push(localizer.ConnectionInterrupted(station.Name, ex.Message));
                        NoteReconnect(station.Id, "Waiting for reconnect");
                        break;
                    }
                }

                response.Dispose();
                if (!cancellationToken.IsCancellationRequested)
                {
                    await WaitBeforeRetryAsync(cancellationToken);
                }
            }
        }
        finally
        {
            await FinalizeOutputAsync(station, settings, paths, logs, output);
        }
    }

    private async Task RecordHttpLoopAsync(
        Station station,
        AppSettings settings,
        AppPaths paths,
        LogBus logs,
        CancellationToken cancellationToken)
    {
        OutputSession? output = null;
        var localizer = AppLocalizer.For(settings.Language);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UpdateSnapshot(station.Id, value =>
                {
                    value.StateLabel = value.OutputPath is not null ? "Reconnecting" : "Connecting";
                });

                OpenStreamSession? source = null;
                try
                {
                    source = await OpenHttpStreamAsync(station, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logs.Push(localizer.ConnectionFailed(station.Name, ex.Message));
                    NoteReconnect(station.Id, "Waiting for reconnect");
                    await WaitBeforeRetryAsync(cancellationToken);
                    continue;
                }

                using (source)
                {
                    var responseStream = source.Stream;
                    var contentType = source.ContentType;
                    var initialBytes = await ReadInitialBytesAsync(responseStream, cancellationToken);
                    if (initialBytes.Length == 0)
                    {
                        logs.Push(localizer.StreamProducedNoDataRetrying(station.Name));
                        NoteReconnect(station.Id, "Waiting for reconnect");
                        await WaitBeforeRetryAsync(cancellationToken);
                        continue;
                    }

                    if (output is null)
                    {
                        var probe = StreamProbeService.ProbeStream(station.Url, contentType, initialBytes);
                        if (probe.Protocol == StreamProtocol.Hls)
                        {
                            await RecordHlsLoopAsync(station, settings, paths, logs, cancellationToken);
                            return;
                        }

                        LogUnknownFormatDetails(logs, localizer, station.Name, station.Url, probe, initialBytes);
                        var outputPath = FileNameTemplate.BuildOutputPath(paths, settings, station, probe.Extension, DateTimeOffset.Now);
                        var file = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read, 81920, useAsync: true);
                        await file.WriteAsync(initialBytes, cancellationToken);
                        output = new OutputSession(file, outputPath, probe.Format);
                        MarkOutputStarted(station, probe.Format, outputPath);
                        IncrementBytesWritten(station.Id, initialBytes.LongLength);
                        logs.Push(localizer.RecordingStarted(station.Name, outputPath, probe.Format.GetDisplayName(settings.Language)));
                    }
                    else
                    {
                        await output.File.WriteAsync(initialBytes, cancellationToken);
                        IncrementBytesWritten(station.Id, initialBytes.LongLength);
                    }

                    var chunkBuffer = new byte[8192];
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            var read = await responseStream.ReadAsync(chunkBuffer.AsMemory(0, chunkBuffer.Length), cancellationToken);
                            if (read == 0)
                            {
                                logs.Push(localizer.ConnectionEndedRetrying(station.Name));
                                NoteReconnect(station.Id, "Waiting for reconnect");
                                break;
                            }

                            await output.File.WriteAsync(chunkBuffer.AsMemory(0, read), cancellationToken);
                            IncrementBytesWritten(station.Id, read);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            logs.Push(localizer.ConnectionInterrupted(station.Name, ex.Message));
                            NoteReconnect(station.Id, "Waiting for reconnect");
                            break;
                        }
                    }
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    await WaitBeforeRetryAsync(cancellationToken);
                }
            }
        }
        finally
        {
            await FinalizeOutputAsync(station, settings, paths, logs, output);
        }
    }

    private async Task RecordHlsLoopAsync(
        Station station,
        AppSettings settings,
        AppPaths paths,
        LogBus logs,
        CancellationToken cancellationToken)
    {
        OutputSession? output = null;
        var playlistUrl = new Uri(station.Url, UriKind.Absolute);
        var seenSegments = new HashSet<string>(StringComparer.Ordinal);
        var segmentOrder = new Queue<string>();
        var localizer = AppLocalizer.For(settings.Language);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string playlistBody;
                try
                {
                    using var request = BuildRequest(station, playlistUrl.ToString());
                    using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    playlistBody = await response.Content.ReadAsStringAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logs.Push(localizer.HlsPlaylistError(station.Name, ex.Message));
                    NoteReconnect(station.Id, "Waiting for playlist");
                    await WaitBeforeRetryAsync(cancellationToken);
                    continue;
                }

                var parsed = ParsePlaylist(playlistUrl, playlistBody);
                if (parsed.MasterPlaylist is not null)
                {
                    playlistUrl = parsed.MasterPlaylist;
                    continue;
                }

                var wroteSegment = false;
                foreach (var segmentUrl in parsed.Segments)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var key = segmentUrl.ToString();
                    if (!seenSegments.Add(key))
                    {
                        continue;
                    }

                    try
                    {
                        using var request = BuildRequest(station, segmentUrl.ToString());
                        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        response.EnsureSuccessStatusCode();
                        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                        if (bytes.Length == 0)
                        {
                            continue;
                        }

                        if (output is null)
                        {
                            var contentType = response.Content.Headers.ContentType?.MediaType;
                            var probe = StreamProbeService.ProbeStream(segmentUrl.ToString(), contentType, bytes);
                            LogUnknownFormatDetails(logs, localizer, station.Name, segmentUrl.ToString(), probe, bytes);
                            var outputPath = FileNameTemplate.BuildOutputPath(paths, settings, station, probe.Extension, DateTimeOffset.Now);
                            var file = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read, 81920, useAsync: true);
                            await file.WriteAsync(bytes, cancellationToken);
                            output = new OutputSession(file, outputPath, probe.Format);

                            MarkOutputStarted(station, probe.Format, outputPath);
                            IncrementBytesWritten(station.Id, bytes.LongLength);
                            logs.Push(localizer.HlsRecordingStarted(station.Name, outputPath, probe.Format.GetDisplayName(settings.Language)));
                        }
                        else
                        {
                            await output.File.WriteAsync(bytes, cancellationToken);
                            IncrementBytesWritten(station.Id, bytes.LongLength);
                        }

                        segmentOrder.Enqueue(key);
                        while (segmentOrder.Count > SegmentHistoryLimit)
                        {
                            seenSegments.Remove(segmentOrder.Dequeue());
                        }

                        wroteSegment = true;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logs.Push(localizer.HlsSegmentError(station.Name, ex.Message));
                    }
                }

                if (!wroteSegment)
                {
                    UpdateSnapshot(station.Id, value => value.StateLabel = "Waiting for HLS segments");
                }

                await Task.Delay(parsed.PollInterval, cancellationToken);
            }
        }
        finally
        {
            await FinalizeOutputAsync(station, settings, paths, logs, output);
        }
    }

    private async Task FinalizeOutputAsync(
        Station station,
        AppSettings settings,
        AppPaths paths,
        LogBus logs,
        OutputSession? output)
    {
        if (output is not null)
        {
            await output.File.FlushAsync(CancellationToken.None);
            await output.File.DisposeAsync();

            var finalOutputPath = output.Path;
            if (output.Format == StreamFormat.AacRaw && settings.RemuxRawAacToM4A)
            {
                finalOutputPath = await Mp4BoxRemuxer.RemuxRawAacAsync(paths, logs, settings.Language, output.Path);
            }

            UpdateSnapshot(station.Id, value =>
            {
                value.Active = false;
                value.StateLabel = "Stopped";
                value.OutputPath = finalOutputPath;
            });
            logs.Push(AppLocalizer.For(settings.Language).RecordingStopped(station.Name));
        }
        else
        {
            UpdateSnapshot(station.Id, value =>
            {
                value.Active = false;
                value.StateLabel = "Stopped";
            });
        }
    }

    private HttpRequestMessage BuildRequest(Station station, string? url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url ?? station.Url);
        request.Headers.TryAddWithoutValidation("Icy-MetaData", "0");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

        if (station.Credentials is not null && !string.IsNullOrWhiteSpace(station.Credentials.Username))
        {
            var raw = $"{station.Credentials.Username}:{station.Credentials.Password}";
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw)));
        }

        return request;
    }

    private HttpRequestMessage BuildMmshRequest(Station station, int requestContext)
    {
        var request = BuildRequest(station, NormalizeMmshRequestUrl(station.Url));
        request.Headers.UserAgent.Clear();
        request.Headers.TryAddWithoutValidation("User-Agent", "NSPlayer/12.00.19041.7058");
        request.Headers.TryAddWithoutValidation("Pragma", $"xClientGUID={{{Guid.NewGuid():D}}}");
        request.Headers.TryAddWithoutValidation("Pragma", $"no-cache,rate=1.000000,stream-time=0,stream-offset=0:0,request-context={requestContext},max-duration=0");
        request.Headers.TryAddWithoutValidation("Pragma", "xPlayStrm=1");
        request.Headers.TryAddWithoutValidation("Pragma", "stream-switch-count=1");
        request.Headers.TryAddWithoutValidation("Pragma", "stream-switch-entry=ffff:1:0");
        request.Headers.ConnectionClose = true;
        return request;
    }

    private async Task<OpenStreamSession> OpenHttpStreamAsync(Station station, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        try
        {
            using var request = BuildRequest(station, null);
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return OpenStreamSession.FromHttp(response, responseStream, response.Content.Headers.ContentType?.MediaType);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            response?.Dispose();

            try
            {
                var icyResponse = await IcyStreamClient.OpenAsync($"StreamRecorder/{currentVersion}", station, cancellationToken);
                return OpenStreamSession.FromIcy(icyResponse);
            }
            catch (Exception icyEx) when (icyEx is not OperationCanceledException)
            {
                throw new InvalidOperationException($"{ex.Message} | ICY fallback failed: {icyEx.Message}", ex);
            }
        }
    }

    private static string NormalizeMmshRequestUrl(string url)
    {
        var uri = new Uri(url, UriKind.Absolute);
        if (!uri.Scheme.Equals("mms", StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals("mmsh", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = Uri.UriSchemeHttp,
            Port = uri.IsDefaultPort ? 80 : uri.Port,
            Path = string.IsNullOrWhiteSpace(uri.AbsolutePath) ? "/" : uri.AbsolutePath,
        };

        return builder.Uri.ToString();
    }

    private static async Task<byte[]> ReadInitialBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[4096];

        while (memory.Length < InitialProbeBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            if (memory.Length >= 4096)
            {
                break;
            }
        }

        return memory.ToArray();
    }

    private static async Task<byte[]> ReadInitialMmshBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();

        while (memory.Length < InitialProbeBytes)
        {
            var chunk = await MmshStreamReader.ReadChunkAsync(stream, cancellationToken);
            if (chunk is null)
            {
                break;
            }

            if (chunk.Data.Length == 0)
            {
                continue;
            }

            await memory.WriteAsync(chunk.Data, cancellationToken);
            if (memory.Length >= 4096)
            {
                break;
            }
        }

        return memory.ToArray();
    }

    private void MarkOutputStarted(Station station, StreamFormat format, string outputPath)
    {
        UpdateSnapshot(station.Id, value =>
        {
            value.StationId = station.Id;
            value.StationName = station.Name;
            value.Active = true;
            value.Format = format;
            value.OutputPath = outputPath;
            value.StateLabel = $"Recording {format.GetDisplayName()}";
            value.LastError = null;
            value.StartedAt ??= DateTimeOffset.Now;
        });
    }

    private void IncrementBytesWritten(Guid stationId, long bytes)
    {
        UpdateSnapshot(stationId, value => value.BytesWritten += bytes);
    }

    private void NoteReconnect(Guid stationId, string stateLabel)
    {
        UpdateSnapshot(stationId, value =>
        {
            value.ReconnectCount += 1;
            value.StateLabel = stateLabel;
        });
    }

    private void UpdateSnapshot(Guid stationId, Action<RecordingSnapshot> update)
    {
        snapshots.AddOrUpdate(
            stationId,
            _ =>
            {
                var snapshot = new RecordingSnapshot { StationId = stationId };
                update(snapshot);
                return snapshot;
            },
            (_, existing) =>
            {
                update(existing);
                return existing;
            });

        RaiseSnapshotsChanged();
    }

    private void RaiseSnapshotsChanged()
    {
        SnapshotsChanged?.Invoke();
    }

    private static RecordingSnapshot CloneSnapshot(RecordingSnapshot snapshot)
    {
        return new RecordingSnapshot
        {
            StationId = snapshot.StationId,
            StationName = snapshot.StationName,
            Active = snapshot.Active,
            StateLabel = snapshot.StateLabel,
            Format = snapshot.Format,
            OutputPath = snapshot.OutputPath,
            BytesWritten = snapshot.BytesWritten,
            ReconnectCount = snapshot.ReconnectCount,
            LastError = snapshot.LastError,
            StartedAt = snapshot.StartedAt,
        };
    }

    private static async Task WaitBeforeRetryAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
    }

    private static PlaylistParseResult ParsePlaylist(Uri baseUri, string body)
    {
        if (!body.TrimStart().StartsWith("#EXTM3U", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Playlist is not a valid M3U8 document.");
        }

        if (body.Contains("#EXT-X-STREAM-INF", StringComparison.Ordinal))
        {
            long bestBandwidth = -1;
            Uri? bestVariant = null;
            var lines = body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index].Trim();
                if (!line.StartsWith("#EXT-X-STREAM-INF", StringComparison.Ordinal))
                {
                    continue;
                }

                var bandwidth = ParseBandwidth(line);
                for (var next = index + 1; next < lines.Length; next++)
                {
                    var target = lines[next].Trim();
                    if (string.IsNullOrWhiteSpace(target) || target.StartsWith('#'))
                    {
                        continue;
                    }

                    if (bandwidth >= bestBandwidth)
                    {
                        bestBandwidth = bandwidth;
                        bestVariant = new Uri(baseUri, target);
                    }
                    break;
                }
            }

            if (bestVariant is null)
            {
                throw new InvalidOperationException("Master playlist does not contain variants.");
            }

            return new PlaylistParseResult(bestVariant, [], TimeSpan.FromSeconds(2));
        }

        var segments = new List<Uri>();
        var targetDuration = 4;

        foreach (var rawLine in body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("#EXT-X-TARGETDURATION:", StringComparison.Ordinal))
            {
                var secondsText = line.Substring("#EXT-X-TARGETDURATION:".Length).Trim();
                if (int.TryParse(secondsText, out var seconds))
                {
                    targetDuration = Math.Max(1, seconds);
                }
                continue;
            }

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            segments.Add(new Uri(baseUri, line));
        }

        return new PlaylistParseResult(null, segments, TimeSpan.FromSeconds(Math.Max(1, targetDuration / 2)));
    }

    private static long ParseBandwidth(string line)
    {
        foreach (var part in line.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split(new[] { '=' }, 2, StringSplitOptions.None);
            if (pieces.Length == 2 && string.Equals(pieces[0].Trim(), "BANDWIDTH", StringComparison.Ordinal))
            {
                if (long.TryParse(pieces[1].Trim(), out var value))
                {
                    return value;
                }
            }
        }

        return 0;
    }

    private static void LogUnknownFormatDetails(
        LogBus logs,
        AppLocalizer localizer,
        string stationName,
        string sourceUrl,
        StreamProbe probe,
        byte[] firstBytes)
    {
        if (probe.Format != StreamFormat.Unknown)
        {
            return;
        }

        var mime = string.IsNullOrWhiteSpace(probe.Mime) ? "(none)" : probe.Mime;
        logs.Push(localizer.UnknownStreamFormat(stationName, sourceUrl, mime, DescribeByteSample(firstBytes)));
    }

    private static string DescribeByteSample(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return "(empty sample)";
        }

        var previewLength = Math.Min(bytes.Length, 16);
        var preview = string.Join(" ", bytes.Take(previewLength).Select(static value => value.ToString("X2")));
        var truncated = bytes.Length > previewLength ? " ..." : string.Empty;
        return $"{preview}{truncated} ({bytes.Length} bytes sampled)";
    }

    public void Dispose()
    {
        StopAll();
        httpClient.Dispose();
    }

    private sealed record RecordingSession(CancellationTokenSource Cancellation, Task Task);

    private sealed record OutputSession(FileStream File, string Path, StreamFormat Format);

    private sealed record PlaylistParseResult(Uri? MasterPlaylist, IReadOnlyList<Uri> Segments, TimeSpan PollInterval);

    private sealed class OpenStreamSession : IDisposable
    {
        private readonly HttpResponseMessage? response;
        private readonly IcyStreamResponse? icyResponse;

        private OpenStreamSession(Stream stream, string? contentType, HttpResponseMessage? response, IcyStreamResponse? icyResponse)
        {
            Stream = stream;
            ContentType = contentType;
            this.response = response;
            this.icyResponse = icyResponse;
        }

        public Stream Stream { get; }

        public string? ContentType { get; }

        public static OpenStreamSession FromHttp(HttpResponseMessage response, Stream stream, string? contentType)
        {
            return new OpenStreamSession(stream, contentType, response, null);
        }

        public static OpenStreamSession FromIcy(IcyStreamResponse response)
        {
            return new OpenStreamSession(response.Stream, response.ContentType, null, response);
        }

        public void Dispose()
        {
            if (response is not null)
            {
                response.Dispose();
                return;
            }

            icyResponse?.Dispose();
        }
    }
}
