using System.Collections.Concurrent;
using StreamRecorder.Core.Localization;
using StreamRecorder.Core.Logging;
using StreamRecorder.Core.Models;

namespace StreamRecorder.Core.Scheduling;

public sealed class SchedulerService : IDisposable
{
    private readonly ConcurrentDictionary<string, DateTime> lastRuns = new();
    private readonly ConcurrentDictionary<Guid, bool> hourlyDesiredStates = new();
    private CancellationTokenSource? cancellation;
    private Task? loopTask;

    public void Start(
        Func<IReadOnlyList<ScheduleEntry>> schedulesProvider,
        Func<IReadOnlyList<Station>> stationsProvider,
        Func<Guid, Station?> stationProvider,
        Func<string> languageProvider,
        Func<string> rootDirectoryProvider,
        Func<Guid, bool> isRecording,
        Func<Guid, Task> startRecordingAsync,
        Action<Guid> stopRecording,
        LogBus logs)
    {
        Stop();

        var tokenSource = new CancellationTokenSource();
        cancellation = tokenSource;
        var token = tokenSource.Token;
        loopTask = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var now = DateTime.Now;
                    var stations = stationsProvider();
                    var hourlyStationIds = new HashSet<Guid>();

                    foreach (var station in stations)
                    {
                        if (!station.HasActiveHourlyRecordingPlan)
                        {
                            if (hourlyDesiredStates.TryRemove(station.Id, out var wasDesired)
                                && wasDesired
                                && isRecording(station.Id))
                            {
                                stopRecording(station.Id);
                            }

                            continue;
                        }

                        hourlyStationIds.Add(station.Id);
                        var shouldRecord = station.ShouldRecordDuringHour(now.Hour);
                        var hadPreviousState = hourlyDesiredStates.TryGetValue(station.Id, out var previousState);
                        hourlyDesiredStates[station.Id] = shouldRecord;
                        if (shouldRecord && !isRecording(station.Id))
                        {
                            await startRecordingAsync(station.Id);
                            if (isRecording(station.Id))
                            {
                                var localizer = AppLocalizer.For(languageProvider(), rootDirectoryProvider());
                                logs.Push(localizer.ScheduleStartedRecording(station.Name));
                            }
                        }
                        else if (!shouldRecord
                            && (!hadPreviousState || previousState)
                            && isRecording(station.Id))
                        {
                            stopRecording(station.Id);
                            var localizer = AppLocalizer.For(languageProvider(), rootDirectoryProvider());
                            logs.Push(localizer.ScheduleStoppedRecording(station.Name));
                        }
                    }

                    foreach (var trackedStationId in hourlyDesiredStates.Keys.ToArray())
                    {
                        if (hourlyStationIds.Contains(trackedStationId))
                        {
                            continue;
                        }

                        hourlyDesiredStates.TryRemove(trackedStationId, out _);
                    }

                    foreach (var schedule in schedulesProvider())
                    {
                        if (!schedule.Enabled)
                        {
                            continue;
                        }

                        if (hourlyStationIds.Contains(schedule.StationId))
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

                    await Task.Delay(TimeSpan.FromSeconds(1), token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }, token);
    }

    public void Stop()
    {
        var tokenSource = cancellation;
        cancellation = null;
        loopTask = null;
        tokenSource?.Cancel();
        hourlyDesiredStates.Clear();
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
