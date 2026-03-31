using System.Collections.Concurrent;
using StreamRecorder.Core.Logging;
using StreamRecorder.Core.Models;

namespace StreamRecorder.Core.Scheduling;

public sealed class SchedulerService : IDisposable
{
    private readonly ConcurrentDictionary<Guid, DateTime> lastRuns = new();
    private PeriodicTimer? timer;
    private CancellationTokenSource? cancellation;
    private Task? loopTask;

    public void Start(
        Func<IReadOnlyList<ScheduleEntry>> schedulesProvider,
        Func<Guid, Station?> stationProvider,
        Func<Guid, bool> isRecording,
        Func<Guid, Task> startRecordingAsync,
        Action<Guid> stopRecording,
        LogBus logs)
    {
        Stop();

        cancellation = new CancellationTokenSource();
        timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        loopTask = Task.Run(async () =>
        {
            while (await timer.WaitForNextTickAsync(cancellation.Token))
            {
                var now = DateTime.Now;
                foreach (var schedule in schedulesProvider())
                {
                    if (!schedule.Enabled || schedule.DayOfWeek != now.DayOfWeek)
                    {
                        continue;
                    }

                    if (schedule.Hour != now.Hour || schedule.Minute != now.Minute || schedule.Second != now.Second)
                    {
                        continue;
                    }

                    if (lastRuns.TryGetValue(schedule.Id, out var lastRun) && (now - lastRun) < TimeSpan.FromSeconds(1))
                    {
                        continue;
                    }

                    lastRuns[schedule.Id] = now;
                    var station = stationProvider(schedule.StationId);
                    if (station is null)
                    {
                        continue;
                    }

                    if (schedule.Action == ScheduleAction.StartRecording)
                    {
                        if (!isRecording(station.Id))
                        {
                            await startRecordingAsync(station.Id);
                            logs.Push($"Schedule started recording: {station.Name}");
                        }
                    }
                    else if (isRecording(station.Id))
                    {
                        stopRecording(station.Id);
                        logs.Push($"Schedule stopped recording: {station.Name}");
                    }
                }
            }
        }, cancellation.Token);
    }

    public void Stop()
    {
        cancellation?.Cancel();
        timer?.Dispose();
        timer = null;
        cancellation = null;
        loopTask = null;
    }

    public void Dispose()
    {
        Stop();
    }
}
