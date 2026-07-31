using System.Collections.Concurrent;
using StreamRecorder.Core.Localization;
using StreamRecorder.Core.Logging;
using StreamRecorder.Core.Models;

namespace StreamRecorder.Core.Scheduling;

public sealed class SchedulerService : IDisposable
{
    private readonly ConcurrentDictionary<string, DateTime> lastRuns = new();
    private CancellationTokenSource? cancellation;
    private Task? loopTask;

    public void Start(
        Func<IReadOnlyList<ScheduleEntry>> schedulesProvider,
        Func<Guid, Station?> stationProvider,
        Func<string> languageProvider,
        Func<string> rootDirectoryProvider,
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
                    if (!schedule.Enabled)
                    {
                        continue;
                    }

                    var station = stationProvider(schedule.StationId);
                    if (station is null)
                    {
                        continue;
                    }

                    var localizer = AppLocalizer.For(languageProvider(), rootDirectoryProvider());

                    if (IsBoundaryDue(schedule, now, ScheduleBoundary.Start)
                        && MarkBoundaryRun(schedule.Id, ScheduleBoundary.Start, now)
                        && !isRecording(station.Id))
                    {
                        await startRecordingAsync(station.Id);
                        logs.Push(localizer.ScheduleStartedRecording(station.Name));
                    }

                    if (IsBoundaryDue(schedule, now, ScheduleBoundary.Stop)
                        && MarkBoundaryRun(schedule.Id, ScheduleBoundary.Stop, now)
                        && isRecording(station.Id))
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

    private bool MarkBoundaryRun(Guid scheduleId, ScheduleBoundary boundary, DateTime now)
    {
        var key = scheduleId.ToString("D") + "|" + boundary;
        if (lastRuns.TryGetValue(key, out var lastRun) && (now - lastRun) < TimeSpan.FromSeconds(1))
        {
            return false;
        }

        lastRuns[key] = now;
        return true;
    }

    private static bool IsBoundaryDue(ScheduleEntry schedule, DateTime now, ScheduleBoundary boundary)
    {
        var target = boundary == ScheduleBoundary.Start
            ? schedule.GetStartTime()
            : schedule.GetEndTime();

        if (target.Hours != now.Hour || target.Minutes != now.Minute || target.Seconds != now.Second)
        {
            return false;
        }

        var days = schedule.GetDays();
        if (boundary == ScheduleBoundary.Start)
        {
            return days.Contains(now.DayOfWeek);
        }

        if (!schedule.CrossesMidnight())
        {
            return days.Contains(now.DayOfWeek);
        }

        return days.Contains(PreviousDay(now.DayOfWeek));
    }

    private static DayOfWeek PreviousDay(DayOfWeek day)
    {
        return day == DayOfWeek.Sunday ? DayOfWeek.Saturday : (DayOfWeek)((int)day - 1);
    }

    private enum ScheduleBoundary
    {
        Start,
        Stop,
    }
}
