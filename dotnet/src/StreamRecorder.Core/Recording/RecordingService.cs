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
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StreamReadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DisposeWaitTimeout = TimeSpan.FromSeconds(5);

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

        var localizer = AppLocalizer.For(settings.Language, paths.RootDirectory);
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
                if (sessions.TryRemove(station.Id, out var removed))
                {
                    removed.Cancellation.Dispose();
                }

                RaiseSnapshotsChanged();
            }
        }, CancellationToken.None);

        sessions[station.Id] = new RecordingSession(cts, task, station);
        return Task.CompletedTask;
    }

    public void SetSaveStreamTitles(Guid stationId, bool enabled)
    {
        if (sessions.TryGetValue(stationId, out var session))
        {
            session.Station.SaveStreamTitles = enabled;
        }
    }

    public void Stop(Guid stationId)
    {
        if (sessions.TryGetValue(stationId, out var session))
        {
            session.Cancellation.Cancel();
            UpdateSnapshot(stationId, value => value.StateLabel = "Stopping");
        }
    }

    public void StopAll()
    {
        CancelAll();
    }

    public bool StopAllAndWait(TimeSpan timeout)
    {
        var tasks = CancelAll();
        return WaitForTasks(tasks, timeout);
    }

    private List<Task> CancelAll()
    {
        var activeSessions = sessions.ToArray();
        foreach (var pair in activeSessions)
        {
            pair.Value.Cancellation.Cancel();
            UpdateSnapshot(pair.Key, value => value.StateLabel = "Stopping");
        }

        return activeSessions.Select(static pair => pair.Value.Task).ToList();
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
        var localizer = AppLocalizer.For(settings.Language, paths.RootDirectory);
        var splitInterval = GetSplitInterval(settings);
        var pendingSegmentFinalizations = new List<Task>();
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
                    response = await SendWithTimeoutAsync(request, cancellationToken);
                    response.EnsureSuccessStatusCode();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logs.Push(localizer.ConnectionFailed(station.Name, ex.Message));
                    NoteReconnect(station.Id, "Waiting for reconnect");
                    await WaitBeforeRetryAsync(cancellationToken);
                    continue;
                }

                using var responseCancellationRegistration = cancellationToken.Register(static state => ((IDisposable)state!).Dispose(), response);
                using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var contentType = response.Content.Headers.ContentType?.MediaType;
                byte[] initialBytes;
                try
                {
                    initialBytes = await ReadInitialMmshBytesAsync(responseStream, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    response.Dispose();
                    logs.Push(localizer.ConnectionInterrupted(station.Name, ex.Message));
                    NoteReconnect(station.Id, "Waiting for reconnect");
                    await WaitBeforeRetryAsync(cancellationToken);
                    continue;
                }

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
                    output = await WriteBytesToOutputAsync(
                        station,
                        settings,
                        paths,
                        logs,
                        localizer,
                        output,
                        splitInterval,
                        pendingSegmentFinalizations,
                        () => probe,
                        station.Url,
                        initialBytes,
                        hls: false,
                        cancellationToken);
                }
                else
                {
                    output = await WriteBytesToOutputAsync(
                        station,
                        settings,
                        paths,
                        logs,
                        localizer,
                        output,
                        splitInterval,
                        pendingSegmentFinalizations,
                        () => StreamProbeService.ProbeStream(station.Url, contentType, initialBytes),
                        station.Url,
                        initialBytes,
                        hls: false,
                        cancellationToken);
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var chunk = await MmshStreamReader.ReadChunkAsync(responseStream, StreamReadTimeout, cancellationToken);
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

                        output = await WriteBytesToOutputAsync(
                            station,
                            settings,
                            paths,
                            logs,
                            localizer,
                            output,
                            splitInterval,
                            pendingSegmentFinalizations,
                            () => StreamProbeService.ProbeStream(station.Url, contentType, chunk.Data),
                            station.Url,
                            chunk.Data,
                            hls: false,
                            cancellationToken);
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
            await FinalizeOutputAsync(station, settings, paths, logs, output, markStopped: true);
            await AwaitPendingSegmentFinalizationsAsync(logs, pendingSegmentFinalizations);
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
        var localizer = AppLocalizer.For(settings.Language, paths.RootDirectory);
        var splitInterval = GetSplitInterval(settings);
        var pendingSegmentFinalizations = new List<Task>();
        var pendingTitles = new Queue<StreamTitleEvent>();

        void HandleStreamTitle(string title)
        {
            if (!station.SaveStreamTitles && !settings.CreateCueSheets)
            {
                output?.SetSaveStreamTitles(false);
                pendingTitles.Clear();
                return;
            }

            pendingTitles.Enqueue(new StreamTitleEvent(DateTimeOffset.Now, title));
        }

        void FlushPendingTitles()
        {
            if (output is null || (!station.SaveStreamTitles && !settings.CreateCueSheets))
            {
                pendingTitles.Clear();
                return;
            }

            try
            {
                output.SetSaveStreamTitles(station.SaveStreamTitles);
                output.SetCreateCueSheet(settings.CreateCueSheets);
                while (pendingTitles.Count > 0)
                {
                    output.WriteTitle(pendingTitles.Dequeue());
                }
            }
            catch (Exception ex)
            {
                logs.Push(localizer.StreamTitlesFileError(station.Name, ex.Message));
                pendingTitles.Clear();
            }
        }

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
                using (cancellationToken.Register(static state => ((IDisposable)state!).Dispose(), source))
                {
                    var responseStream = source.Stream;
                    var metadataReader = new IcyMetadataReader(responseStream, source.MetadataInterval, HandleStreamTitle);
                    var contentType = source.ContentType;
                    byte[] initialBytes;
                    try
                    {
                        initialBytes = await ReadInitialBytesAsync(metadataReader, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logs.Push(localizer.ConnectionInterrupted(station.Name, ex.Message));
                        NoteReconnect(station.Id, "Waiting for reconnect");
                        await WaitBeforeRetryAsync(cancellationToken);
                        continue;
                    }

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

                        output = await WriteBytesToOutputAsync(
                            station,
                            settings,
                            paths,
                            logs,
                            localizer,
                            output,
                            splitInterval,
                            pendingSegmentFinalizations,
                            () => probe,
                            station.Url,
                            initialBytes,
                            hls: false,
                            cancellationToken);
                    }
                    else
                    {
                        output = await WriteBytesToOutputAsync(
                            station,
                            settings,
                            paths,
                            logs,
                            localizer,
                            output,
                            splitInterval,
                            pendingSegmentFinalizations,
                            () => StreamProbeService.ProbeStream(station.Url, contentType, initialBytes),
                            station.Url,
                            initialBytes,
                            hls: false,
                            cancellationToken);
                    }

                    FlushPendingTitles();

                    var chunkBuffer = new byte[8192];
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            var read = await ReadWithTimeoutAsync(
                                () => metadataReader.ReadAsync(chunkBuffer, 0, chunkBuffer.Length, cancellationToken),
                                cancellationToken);
                            if (read == 0)
                            {
                                logs.Push(localizer.ConnectionEndedRetrying(station.Name));
                                NoteReconnect(station.Id, "Waiting for reconnect");
                                break;
                            }

                            var bytes = chunkBuffer.Take(read).ToArray();
                            output = await WriteBytesToOutputAsync(
                                station,
                                settings,
                                paths,
                                logs,
                                localizer,
                                output,
                                splitInterval,
                                pendingSegmentFinalizations,
                                () => StreamProbeService.ProbeStream(station.Url, contentType, bytes),
                                station.Url,
                                bytes,
                                hls: false,
                                cancellationToken);
                            FlushPendingTitles();
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
            FlushPendingTitles();
            await FinalizeOutputAsync(station, settings, paths, logs, output, markStopped: true);
            await AwaitPendingSegmentFinalizationsAsync(logs, pendingSegmentFinalizations);
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
        var localizer = AppLocalizer.For(settings.Language, paths.RootDirectory);
        var splitInterval = GetSplitInterval(settings);
        var pendingSegmentFinalizations = new List<Task>();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string playlistBody;
                try
                {
                    using var request = BuildRequest(station, playlistUrl.ToString());
                    using var response = await SendWithTimeoutAsync(request, cancellationToken);
                    using var cancellationRegistration = cancellationToken.Register(static state => ((IDisposable)state!).Dispose(), response);
                    response.EnsureSuccessStatusCode();
                    playlistBody = await AwaitWithTimeoutAsync(
                        response.Content.ReadAsStringAsync(cancellationToken),
                        RequestTimeout,
                        "Timed out while reading HLS playlist.",
                        cancellationToken);
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
                        using var response = await SendWithTimeoutAsync(request, cancellationToken);
                        using var cancellationRegistration = cancellationToken.Register(static state => ((IDisposable)state!).Dispose(), response);
                        response.EnsureSuccessStatusCode();
                        var bytes = await AwaitWithTimeoutAsync(
                            response.Content.ReadAsByteArrayAsync(cancellationToken),
                            RequestTimeout,
                            "Timed out while reading HLS segment.",
                            cancellationToken);
                        if (bytes.Length == 0)
                        {
                            continue;
                        }

                        if (output is null)
                        {
                            var contentType = response.Content.Headers.ContentType?.MediaType;
                            var probe = StreamProbeService.ProbeStream(segmentUrl.ToString(), contentType, bytes);
                            output = await WriteBytesToOutputAsync(
                                station,
                                settings,
                                paths,
                                logs,
                                localizer,
                                output,
                                splitInterval,
                                pendingSegmentFinalizations,
                                () => probe,
                                segmentUrl.ToString(),
                                bytes,
                                hls: true,
                                cancellationToken);
                        }
                        else
                        {
                            var contentType = response.Content.Headers.ContentType?.MediaType;
                            output = await WriteBytesToOutputAsync(
                                station,
                                settings,
                                paths,
                                logs,
                                localizer,
                                output,
                                splitInterval,
                                pendingSegmentFinalizations,
                                () => StreamProbeService.ProbeStream(segmentUrl.ToString(), contentType, bytes),
                                segmentUrl.ToString(),
                                bytes,
                                hls: true,
                                cancellationToken);
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
            await FinalizeOutputAsync(station, settings, paths, logs, output, markStopped: true);
            await AwaitPendingSegmentFinalizationsAsync(logs, pendingSegmentFinalizations);
        }
    }

    private async Task FinalizeOutputAsync(
        Station station,
        AppSettings settings,
        AppPaths paths,
        LogBus logs,
        OutputSession? output,
        bool markStopped)
    {
        var localizer = AppLocalizer.For(settings.Language, paths.RootDirectory);
        if (output is not null)
        {
            await output.File.FlushAsync(CancellationToken.None);
            output.Dispose();

            var finalOutputPath = output.Path;
            if (output.Format == StreamFormat.AacRaw && settings.RemuxRawAacToM4A)
            {
                finalOutputPath = await Mp4BoxRemuxer.RemuxRawAacAsync(paths, logs, settings.Language, output.Path);
            }

            try
            {
                output.WriteCueSheet(finalOutputPath);
            }
            catch (Exception ex)
            {
                logs.Push(localizer.CueSheetFileError(station.Name, ex.Message));
            }

            if (markStopped)
            {
                UpdateSnapshot(station.Id, value =>
                {
                    value.Active = false;
                    value.StateLabel = "Stopped";
                    value.OutputPath = finalOutputPath;
                });
                logs.Push(localizer.RecordingStopped(station.Name));
            }
            else
            {
                logs.Push(localizer.RecordingSegmentCompleted(station.Name, finalOutputPath));
            }
        }
        else if (markStopped)
        {
            UpdateSnapshot(station.Id, value =>
            {
                value.Active = false;
                value.StateLabel = "Stopped";
            });
        }
    }

    private async Task<OutputSession> WriteBytesToOutputAsync(
        Station station,
        AppSettings settings,
        AppPaths paths,
        LogBus logs,
        AppLocalizer localizer,
        OutputSession? output,
        TimeSpan? splitInterval,
        List<Task> pendingSegmentFinalizations,
        Func<StreamProbe> probeFactory,
        string sourceUrl,
        byte[] bytes,
        bool hls,
        CancellationToken cancellationToken)
    {
        output = RotateOutputIfDue(station, settings, paths, logs, output, splitInterval, pendingSegmentFinalizations);
        if (output is null)
        {
            var probe = probeFactory();
            LogUnknownFormatDetails(logs, localizer, station.Name, sourceUrl, probe, bytes);
            output = OpenOutput(station, settings, paths, logs, localizer, probe.Format, hls);
        }

        output.SetSaveStreamTitles(station.SaveStreamTitles);
        output.SetCreateCueSheet(settings.CreateCueSheets);
        await output.File.WriteAsync(bytes, cancellationToken);
        IncrementBytesWritten(station.Id, bytes.LongLength);
        return output;
    }

    private OutputSession? RotateOutputIfDue(
        Station station,
        AppSettings settings,
        AppPaths paths,
        LogBus logs,
        OutputSession? output,
        TimeSpan? splitInterval,
        List<Task> pendingSegmentFinalizations)
    {
        if (output is null || splitInterval is null)
        {
            return output;
        }

        if (DateTimeOffset.Now - output.StartedAt < splitInterval.Value)
        {
            return output;
        }

        PruneCompletedSegmentFinalizations(logs, pendingSegmentFinalizations);
        pendingSegmentFinalizations.Add(FinalizeOutputAsync(station, settings, paths, logs, output, markStopped: false));
        return null;
    }

    private OutputSession OpenOutput(
        Station station,
        AppSettings settings,
        AppPaths paths,
        LogBus logs,
        AppLocalizer localizer,
        StreamFormat format,
        bool hls)
    {
        var startedAt = DateTimeOffset.Now;
        var outputPath = FileNameTemplate.BuildOutputPath(paths, settings, station, format.GetExtension(), startedAt);
        var file = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read, 81920, useAsync: true);
        var output = new OutputSession(file, outputPath, format, startedAt);
        MarkOutputStarted(station, format, outputPath);
        var formatName = format.GetDisplayName(settings.Language);
        logs.Push(hls
            ? localizer.HlsRecordingStarted(station.Name, outputPath, formatName)
            : localizer.RecordingStarted(station.Name, outputPath, formatName));
        return output;
    }

    private static async Task AwaitPendingSegmentFinalizationsAsync(LogBus logs, List<Task> pendingSegmentFinalizations)
    {
        foreach (var task in pendingSegmentFinalizations.ToArray())
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                logs.Push($"Recording segment finalization failed: {ex.Message}");
            }
        }
    }

    private static void PruneCompletedSegmentFinalizations(LogBus logs, List<Task> pendingSegmentFinalizations)
    {
        for (var index = pendingSegmentFinalizations.Count - 1; index >= 0; index--)
        {
            var task = pendingSegmentFinalizations[index];
            if (!task.IsCompleted)
            {
                continue;
            }

            if (task.IsFaulted && task.Exception is not null)
            {
                logs.Push($"Recording segment finalization failed: {task.Exception.GetBaseException().Message}");
            }

            pendingSegmentFinalizations.RemoveAt(index);
        }
    }

    private static TimeSpan? GetSplitInterval(AppSettings settings)
    {
        if (!settings.SplitRecordingsEnabled)
        {
            return null;
        }

        var hours = Math.Max(0, settings.SplitHours);
        var minutes = Math.Max(0, Math.Min(59, settings.SplitMinutes));
        var seconds = Math.Max(0, Math.Min(59, settings.SplitSeconds));
        var totalSeconds = checked((hours * 3600L) + (minutes * 60L) + seconds);
        return totalSeconds <= 0 ? null : TimeSpan.FromSeconds(totalSeconds);
    }

    private async Task<HttpResponseMessage> SendWithTimeoutAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sendTask = httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return await AwaitWithTimeoutAsync(sendTask, RequestTimeout, "Timed out while waiting for stream response headers.", cancellationToken);
    }

    private static async Task<T> AwaitWithTimeoutAsync<T>(
        Task<T> task,
        TimeSpan timeout,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        _ = task.ContinueWith(static completedTask => _ = completedTask.Exception, TaskContinuationOptions.OnlyOnFaulted);
        var delayTask = Task.Delay(timeout, cancellationToken);
        var completed = await Task.WhenAny(task, delayTask);
        if (completed == task)
        {
            return await task;
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException(timeoutMessage);
    }

    private HttpRequestMessage BuildRequest(Station station, string? url, bool requestIcyMetadata = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url ?? station.Url);
        if (requestIcyMetadata)
        {
            request.Headers.TryAddWithoutValidation("Icy-MetaData", "1");
        }
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
        var request = BuildRequest(station, NormalizeMmshRequestUrl(station.Url), requestIcyMetadata: false);
        request.Headers.UserAgent.Clear();
        request.Headers.TryAddWithoutValidation("User-Agent", "NSPlayer/12.00.19041.7058");
        var clientGuid = "{" + Guid.NewGuid().ToString("D") + "}";
        request.Headers.TryAddWithoutValidation("Pragma", $"xClientGUID={clientGuid}");
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
            response = await SendWithTimeoutAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return OpenStreamSession.FromHttp(
                response,
                responseStream,
                response.Content.Headers.ContentType?.MediaType,
                ReadIcyMetadataInterval(response));
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

    private static async Task<byte[]> ReadInitialBytesAsync(IcyMetadataReader reader, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[4096];

        while (memory.Length < InitialProbeBytes)
        {
            var read = await ReadWithTimeoutAsync(
                () => reader.ReadAsync(buffer, 0, buffer.Length, cancellationToken),
                cancellationToken);
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
            var chunk = await MmshStreamReader.ReadChunkAsync(stream, StreamReadTimeout, cancellationToken);
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

    private static async Task<int> ReadWithTimeoutAsync(
        Stream stream,
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        return await ReadWithTimeoutAsync(
            () => stream.ReadAsync(buffer, offset, count, cancellationToken),
            cancellationToken);
    }

    private static async Task<int> ReadWithTimeoutAsync(
        Func<Task<int>> readOperation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var readTask = readOperation();
        _ = readTask.ContinueWith(static task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        var delayTask = Task.Delay(StreamReadTimeout, cancellationToken);
        var completed = await Task.WhenAny(readTask, delayTask);
        if (completed == readTask)
        {
            return await readTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException($"No stream data was received for {StreamReadTimeout.TotalSeconds:0} seconds.");
    }

    private static int? ReadIcyMetadataInterval(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("icy-metaint", out var values))
        {
            return null;
        }

        var value = values.FirstOrDefault();
        return int.TryParse(value, out var interval) && interval > 0 ? interval : null;
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
        UpdateSnapshot(stationId, value => value.BytesWritten += bytes, notify: false);
    }

    private void NoteReconnect(Guid stationId, string stateLabel)
    {
        UpdateSnapshot(stationId, value =>
        {
            value.ReconnectCount += 1;
            value.StateLabel = stateLabel;
        });
    }

    private void UpdateSnapshot(Guid stationId, Action<RecordingSnapshot> update, bool notify = true)
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

        if (notify)
        {
            RaiseSnapshotsChanged();
        }
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
        var stopped = StopAllAndWait(DisposeWaitTimeout);
        httpClient.Dispose();
        if (!stopped)
        {
            WaitForTasks(sessions.Values.Select(static session => session.Task).ToList(), TimeSpan.FromSeconds(2));
        }
    }

    private static bool WaitForTasks(IReadOnlyCollection<Task> tasks, TimeSpan timeout)
    {
        if (tasks.Count == 0)
        {
            return true;
        }

        try
        {
            return Task.WaitAll(tasks.ToArray(), timeout);
        }
        catch (AggregateException)
        {
            return tasks.All(static task => task.IsCompleted);
        }
    }

    private sealed record RecordingSession(CancellationTokenSource Cancellation, Task Task, Station Station);

    private sealed record StreamTitleEvent(DateTimeOffset Timestamp, string Title);

    private sealed class OutputSession : IDisposable
    {
        private StreamTitleWriter? titleWriter;
        private CueSheetWriter? cueSheetWriter;

        public OutputSession(FileStream file, string path, StreamFormat format, DateTimeOffset startedAt)
        {
            File = file;
            Path = path;
            Format = format;
            StartedAt = startedAt;
        }

        public FileStream File { get; }

        public string Path { get; }

        public StreamFormat Format { get; }

        public DateTimeOffset StartedAt { get; }

        public void SetSaveStreamTitles(bool enabled)
        {
            if (enabled)
            {
                titleWriter ??= new StreamTitleWriter(System.IO.Path.ChangeExtension(Path, ".txt"));
            }
            else
            {
                titleWriter?.Dispose();
                titleWriter = null;
            }
        }

        public void SetCreateCueSheet(bool enabled)
        {
            if (enabled)
            {
                cueSheetWriter ??= new CueSheetWriter(System.IO.Path.ChangeExtension(Path, ".cue"));
            }
            else
            {
                cueSheetWriter = null;
            }
        }

        public void WriteTitle(StreamTitleEvent titleEvent)
        {
            titleWriter?.Write(titleEvent);
            cueSheetWriter?.Write(titleEvent, StartedAt);
        }

        public void WriteCueSheet(string audioPath)
        {
            cueSheetWriter?.WriteToFile(audioPath);
        }

        public void Dispose()
        {
            try
            {
                titleWriter?.Dispose();
            }
            finally
            {
                File.Dispose();
            }
        }
    }

    private sealed class CueSheetWriter
    {
        private readonly string path;
        private readonly List<CueTrack> tracks = new();
        private string? lastTitle;
        private long lastFrame = -1;

        public CueSheetWriter(string path)
        {
            this.path = path;
        }

        public void Write(StreamTitleEvent titleEvent, DateTimeOffset recordingStartedAt)
        {
            if (string.IsNullOrWhiteSpace(titleEvent.Title)
                || string.Equals(lastTitle, titleEvent.Title, StringComparison.Ordinal))
            {
                return;
            }

            var elapsed = titleEvent.Timestamp - recordingStartedAt;
            var frame = Math.Max(0L, (long)Math.Round(Math.Max(0, elapsed.TotalSeconds) * 75, MidpointRounding.AwayFromZero));
            if (frame <= lastFrame)
            {
                frame = lastFrame + 1;
            }

            tracks.Add(new CueTrack(titleEvent.Title, frame));
            lastTitle = titleEvent.Title;
            lastFrame = frame;
        }

        public void WriteToFile(string audioPath)
        {
            if (tracks.Count == 0)
            {
                return;
            }

            using var writer = new StreamWriter(
                new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, useAsync: false),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.WriteLine("REM Generated by StreamRecorder");
            writer.WriteLine($"FILE \"{Escape(Path.GetFileName(audioPath))}\" {GetFileType(audioPath)}");

            for (var index = 0; index < tracks.Count; index++)
            {
                var track = tracks[index];
                writer.WriteLine($"  TRACK {index + 1:00} AUDIO");
                writer.WriteLine($"    TITLE \"{Escape(track.Title)}\"");
                writer.WriteLine($"    INDEX 01 {FormatIndex(track.Frame)}");
            }
        }

        private static string FormatIndex(long frame)
        {
            var framesPerMinute = 75 * 60;
            var minutes = frame / framesPerMinute;
            var seconds = (frame / 75) % 60;
            var subframes = frame % 75;
            return $"{minutes:00}:{seconds:00}:{subframes:00}";
        }

        private static string GetFileType(string audioPath)
        {
            return System.IO.Path.GetExtension(audioPath).ToLowerInvariant() switch
            {
                ".mp3" => "MP3",
                ".flac" => "FLAC",
                ".ogg" or ".oga" => "OGG",
                ".wav" => "WAVE",
                ".aif" or ".aiff" => "AIFF",
                _ => "BINARY",
            };
        }

        private static string Escape(string value)
        {
            return value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal);
        }

        private sealed record CueTrack(string Title, long Frame);
    }

    private sealed class StreamTitleWriter : IDisposable
    {
        private readonly string path;
        private StreamWriter? writer;
        private string? lastTitle;

        public StreamTitleWriter(string path)
        {
            this.path = path;
        }

        public void Write(StreamTitleEvent titleEvent)
        {
            if (string.IsNullOrWhiteSpace(titleEvent.Title)
                || string.Equals(lastTitle, titleEvent.Title, StringComparison.Ordinal))
            {
                return;
            }

            writer ??= new StreamWriter(
                new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, useAsync: false),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.WriteLine($"{titleEvent.Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}\t{titleEvent.Title}");
            writer.Flush();
            lastTitle = titleEvent.Title;
        }

        public void Dispose()
        {
            writer?.Dispose();
            writer = null;
        }
    }

    private sealed record PlaylistParseResult(Uri? MasterPlaylist, IReadOnlyList<Uri> Segments, TimeSpan PollInterval);

    private sealed class OpenStreamSession : IDisposable
    {
        private readonly HttpResponseMessage? response;
        private readonly IcyStreamResponse? icyResponse;

        private OpenStreamSession(
            Stream stream,
            string? contentType,
            int? metadataInterval,
            HttpResponseMessage? response,
            IcyStreamResponse? icyResponse)
        {
            Stream = stream;
            ContentType = contentType;
            MetadataInterval = metadataInterval;
            this.response = response;
            this.icyResponse = icyResponse;
        }

        public Stream Stream { get; }

        public string? ContentType { get; }

        public int? MetadataInterval { get; }

        public static OpenStreamSession FromHttp(
            HttpResponseMessage response,
            Stream stream,
            string? contentType,
            int? metadataInterval)
        {
            return new OpenStreamSession(stream, contentType, metadataInterval, response, null);
        }

        public static OpenStreamSession FromIcy(IcyStreamResponse response)
        {
            return new OpenStreamSession(response.Stream, response.ContentType, response.MetadataInterval, null, response);
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
