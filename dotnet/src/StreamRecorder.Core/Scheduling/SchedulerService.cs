using System.Collections.Concurrent;
using StreamRecorder.Core.Localization;
using StreamRecorder.Core.Logging;
using StreamRecorder.Core.Models;

namespace StreamRecorder.Core.Scheduling;

public sealed class SchedulerService : IDisposable
{
    private readonly ConcurrentDictionary<Guid, DateTime> lastRuns = new();
    private CancellationTokenSource? cancellation;
    private Task? loopTask;

    public void Start(
        Func<IReadOnlyList<ScheduleEntry>> schedulesProvider,
        Func<Guid, Station?> stationProvider,
        Func<string> languageProvider,
        Func<Guid, bool> isRecording,
        Func<Guid, Task> startRecordingAsync,
        Action<Guid> stopRecording,
        LogBus logs)
    {
        Stop();

        cancellation = new CancellationTokenSource();
        loopTask = Task.Run(async () =>
        {
            while (!cancellation.Token.IsCancellationRequested)
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

                    var localizer = AppLocalizer.For(languageProvider());

                    if (schedule.Action == ScheduleAction.StartRecording)
                    {
                        if (!isRecording(station.Id))
                        {
                            await startRecordingAsync(station.Id);
                            logs.Push(localizer.ScheduleStartedRecording(station.Name));
                        }
                    }
                    else if (isRecording(station.Id))
                    {
                        stopRecording(station.Id);
                        logs.Push(localizer.ScheduleStoppedRecording(station.Name));
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token);
            }
        }, cancellation.Token);
    }

    public void Stop()
    {
        cancellation?.Cancel();
        cancellation = null;
        loopTask = null;
    }

    public void Dispose()
    {
        Stop();
    }
}
