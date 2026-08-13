using StreamRecorder.Core.Configuration;
using StreamRecorder.Core.Localization;
using StreamRecorder.Core.Logging;
using StreamRecorder.Core.Models;

namespace StreamRecorder.Core.Playback;

public sealed class PlaybackService : IDisposable
{
    private const int BassActiveStopped = 0;
    private const int BassActivePlaying = 1;
    private const int BassActiveStalled = 2;
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StalledGracePeriod = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly object gate = new();
    private readonly string rootDirectory;
    private readonly BassPlaybackBackend backend = new();
    private readonly Dictionary<Guid, PlaybackSnapshot> snapshots = new();
    private PlaybackSession? currentSession;
    private bool disposed;

    public PlaybackService(string rootDirectory)
    {
        this.rootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
    }

    public event Action? SnapshotsChanged;

    public bool IsListening(Guid stationId)
    {
        lock (gate)
        {
            return currentSession?.Station.Id == stationId && !currentSession.Completion.Task.IsCompleted;
        }
    }

    public Guid? ListeningStationId
    {
        get
        {
            lock (gate)
            {
                return currentSession?.Station.Id;
            }
        }
    }

    public IReadOnlyDictionary<Guid, PlaybackSnapshot> GetSnapshots()
    {
        lock (gate)
        {
            return snapshots.ToDictionary(pair => pair.Key, pair => CloneSnapshot(pair.Value));
        }
    }

    public PlaybackSnapshot? GetSnapshot(Guid stationId)
    {
        lock (gate)
        {
            return snapshots.TryGetValue(stationId, out var snapshot) ? CloneSnapshot(snapshot) : null;
        }
    }

    public IReadOnlyList<AudioOutputDevice> GetOutputDevices(string defaultDeviceName)
    {
        ThrowIfDisposed();
        var dllPath = FindBassPath();
        return backend.GetDevices(dllPath, defaultDeviceName);
    }

    public async Task StartAsync(
        Station station,
        AppSettings settings,
        AppPaths paths,
        LogBus logs,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        PlaybackSession? oldSession;
        lock (gate)
        {
            oldSession = currentSession;
            if (oldSession is not null && oldSession.Station.Id == station.Id && !oldSession.Completion.Task.IsCompleted)
            {
                return;
            }
        }

        if (oldSession is not null)
        {
            oldSession.Cancellation.Cancel();
            await WaitForSessionAsync(oldSession.Completion.Task).ConfigureAwait(false);
        }

        var localizer = AppLocalizer.For(settings.Language, paths.RootDirectory);
        var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var session = new PlaybackSession(station, sessionCancellation);

        lock (gate)
        {
            currentSession = session;
            snapshots[station.Id] = new PlaybackSnapshot
            {
                StationId = station.Id,
                Active = true,
                State = PlaybackState.Connecting,
                StartedAt = DateTimeOffset.Now,
            };
        }

        RaiseSnapshotsChanged();
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await RunSessionAsync(session, settings, paths, logs, localizer, sessionCancellation.Token).ConfigureAwait(false);
                }
                finally
                {
                    session.Completion.TrySetResult(null);
                    session.Cancellation.Dispose();
                }
            },
            CancellationToken.None);
    }

    public void Stop(Guid stationId)
    {
        lock (gate)
        {
            if (currentSession?.Station.Id != stationId)
            {
                return;
            }

            currentSession.Cancellation.Cancel();
            UpdateSnapshotLocked(stationId, snapshot => snapshot.State = PlaybackState.Stopping);
        }

        RaiseSnapshotsChanged();
    }

    public bool StopAllAndWait(TimeSpan timeout)
    {
        PlaybackSession? session;
        lock (gate)
        {
            session = currentSession;
            session?.Cancellation.Cancel();
        }

        if (session is null)
        {
            return true;
        }

        try
        {
            return session.Completion.Task.Wait(timeout);
        }
        catch (AggregateException)
        {
            return true;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        StopAllAndWait(TimeSpan.FromSeconds(5));
        backend.Dispose();
    }

    private async Task RunSessionAsync(
        PlaybackSession session,
        AppSettings settings,
        AppPaths paths,
        LogBus logs,
        AppLocalizer localizer,
        CancellationToken cancellationToken)
    {
        var station = session.Station;
        uint stream = 0;
        var isFirstAttempt = true;
        var stalledSince = (DateTimeOffset?)null;
        var unsupportedReason = ValidateUrl(station.Url, localizer);

        if (unsupportedReason is not null)
        {
            logs.Push(localizer.PlaybackUnsupported(station.Name, unsupportedReason));
            SetInactive(station.Id, PlaybackState.Error, unsupportedReason);
            return;
        }

        try
        {
            var bassPath = FindBassPath();
            var bassAacPath = FindBassAacPath();
            if (!backend.TryLoad(bassPath, bassAacPath, out var loadError))
            {
                logs.Push(localizer.PlaybackBackendMissing(loadError ?? bassPath));
                SetInactive(station.Id, PlaybackState.Error, loadError);
                return;
            }

            backend.EnsureInitialized(settings.PlaybackDevice);

            while (!cancellationToken.IsCancellationRequested)
            {
                SetState(station.Id, isFirstAttempt ? PlaybackState.Connecting : PlaybackState.Reconnecting);
                isFirstAttempt = false;
                stalledSince = null;

                try
                {
                    stream = backend.CreateStream(station);
                    backend.Play(stream);
                    SetState(station.Id, PlaybackState.Playing);
                    logs.Push(localizer.PlaybackStarted(station.Name));

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                        var activeState = backend.GetActiveState(stream);
                        if (activeState == BassActivePlaying)
                        {
                            stalledSince = null;
                            if (GetSnapshot(station.Id)?.State != PlaybackState.Playing)
                            {
                                SetState(station.Id, PlaybackState.Playing);
                            }

                            continue;
                        }

                        if (activeState == BassActiveStalled)
                        {
                            stalledSince ??= DateTimeOffset.Now;
                            SetState(station.Id, PlaybackState.Reconnecting);
                            if (DateTimeOffset.Now - stalledSince < StalledGracePeriod)
                            {
                                continue;
                            }
                        }

                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    var message = backend.DescribeError(exception);
                    logs.Push(localizer.PlaybackConnectionFailed(station.Name, message));
                }
                finally
                {
                    if (stream != 0)
                    {
                        backend.FreeStream(stream);
                        stream = 0;
                    }
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    logs.Push(localizer.PlaybackReconnecting(station.Name));
                    SetState(station.Id, PlaybackState.Reconnecting);
                    await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            var message = backend.DescribeError(exception);
            logs.Push(localizer.PlaybackConnectionFailed(station.Name, message));
            SetInactive(station.Id, PlaybackState.Error, message);
        }
        finally
        {
            if (stream != 0)
            {
                backend.FreeStream(stream);
            }

            lock (gate)
            {
                if (ReferenceEquals(currentSession, session))
                {
                    currentSession = null;
                }

                if (snapshots.TryGetValue(station.Id, out var snapshot))
                {
                    snapshot.Active = false;
                    if (snapshot.State != PlaybackState.Error)
                    {
                        snapshot.State = cancellationToken.IsCancellationRequested
                            ? PlaybackState.Stopped
                            : snapshot.State;
                    }
                }
            }

            logs.Push(localizer.PlaybackStopped(station.Name));
            RaiseSnapshotsChanged();
        }
    }

    private static string? ValidateUrl(string url, AppLocalizer localizer)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return localizer.PlaybackOnlyHttpMessage;
        }

        if (url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            return localizer.PlaybackHlsNotAvailable;
        }

        return null;
    }

    private void SetState(Guid stationId, PlaybackState state)
    {
        lock (gate)
        {
            UpdateSnapshotLocked(stationId, snapshot =>
            {
                snapshot.Active = true;
                snapshot.State = state;
                if (state != PlaybackState.Error)
                {
                    snapshot.Error = null;
                }
            });
        }

        RaiseSnapshotsChanged();
    }

    private void SetInactive(Guid stationId, PlaybackState state, string? error)
    {
        lock (gate)
        {
            UpdateSnapshotLocked(stationId, snapshot =>
            {
                snapshot.Active = false;
                snapshot.State = state;
                snapshot.Error = error;
            });
        }

        RaiseSnapshotsChanged();
    }

    private void UpdateSnapshotLocked(Guid stationId, Action<PlaybackSnapshot> update)
    {
        if (!snapshots.TryGetValue(stationId, out var snapshot))
        {
            snapshot = new PlaybackSnapshot { StationId = stationId };
            snapshots[stationId] = snapshot;
        }

        update(snapshot);
    }

    private void RaiseSnapshotsChanged()
    {
        SnapshotsChanged?.Invoke();
    }

    private string FindBassPath()
    {
        var architecture = Environment.Is64BitProcess ? "x64" : "x86";
        var candidates = new List<string>();
        var directory = new DirectoryInfo(rootDirectory);
        while (directory is not null)
        {
            candidates.Add(Path.Combine(directory.FullName, "Tools", "BASS", architecture, "bass.dll"));
            candidates.Add(Path.Combine(directory.FullName, "third_party", "BASS", architecture, "bass.dll"));
            directory = directory.Parent;
        }

        return candidates.FirstOrDefault(File.Exists)
            ?? candidates[0];
    }

    private string? FindBassAacPath()
    {
        var architecture = Environment.Is64BitProcess ? "x64" : "x86";
        var candidates = new List<string>();
        var directory = new DirectoryInfo(rootDirectory);
        while (directory is not null)
        {
            candidates.Add(Path.Combine(directory.FullName, "Tools", "BASS", architecture, "bass_aac.dll"));
            candidates.Add(Path.Combine(directory.FullName, "third_party", "BASS", architecture, "bass_aac.dll"));
            directory = directory.Parent;
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task WaitForSessionAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(PlaybackService));
        }
    }

    private static PlaybackSnapshot CloneSnapshot(PlaybackSnapshot source)
    {
        return new PlaybackSnapshot
        {
            StationId = source.StationId,
            Active = source.Active,
            State = source.State,
            Error = source.Error,
            StartedAt = source.StartedAt,
        };
    }

    private sealed class PlaybackSession
    {
        public PlaybackSession(Station station, CancellationTokenSource cancellation)
        {
            Station = station;
            Cancellation = cancellation;
            Completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Station Station { get; }

        public CancellationTokenSource Cancellation { get; }

        public TaskCompletionSource<object?> Completion { get; }
    }
}
