using StreamRecorder.Core.Configuration;
using StreamRecorder.Core.Localization;
using StreamRecorder.Core.Logging;
using StreamRecorder.Core.Models;
using StreamRecorder.Core.Playback;
using StreamRecorder.Core.Recording;
using StreamRecorder.Core.Scheduling;
using StreamRecorder.Core.Updates;

namespace StreamRecorder.Core;

public sealed class StreamRecorderApp : IDisposable
{
    private readonly object gate = new();
    private AppConfig config;

    public StreamRecorderApp(string version, AppPaths paths)
    {
        Version = version;
        Paths = paths;
        config = ConfigStore.LoadOrCreate(paths);
        Logs = new LogBus(paths.LogFilePath);
        Recorder = new RecordingService(version);
        Playback = new PlaybackService(paths.RootDirectory);
        Scheduler = new SchedulerService();
        Updater = new UpdaterService(version);
        StartScheduler();
    }

    public string Version { get; }

    public AppPaths Paths { get; }

    public LogBus Logs { get; }

    public RecordingService Recorder { get; }

    public PlaybackService Playback { get; }

    public SchedulerService Scheduler { get; }

    public UpdaterService Updater { get; }

    public event Action? ConfigChanged;

    public AppLocalizer GetLocalizer()
    {
        lock (gate)
        {
            return AppLocalizer.For(config.Settings.Language, Paths.RootDirectory);
        }
    }

    public AppSettings GetSettings()
    {
        lock (gate)
        {
            return CloneConfig(config).Settings;
        }
    }

    public IReadOnlyList<Station> GetStations()
    {
        lock (gate)
        {
            return CloneConfig(config).Stations;
        }
    }

    public IReadOnlyList<ScheduleEntry> GetSchedules()
    {
        lock (gate)
        {
            return CloneConfig(config).Schedules;
        }
    }

    public Station? GetStation(Guid stationId)
    {
        lock (gate)
        {
            return config.Stations.FirstOrDefault(station => station.Id == stationId) is { } station
                ? CloneStation(station)
                : null;
        }
    }

    public IReadOnlyList<ScheduleEntry> GetSchedulesForStation(Guid stationId)
    {
        lock (gate)
        {
            return config.Schedules
                .Where(schedule => schedule.StationId == stationId)
                .Select(CloneSchedule)
                .ToList();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        lock (gate)
        {
            config.Settings = settings;
            Persist();
        }

        ConfigChanged?.Invoke();
    }

    public void UpsertStation(Station station)
    {
        lock (gate)
        {
            var index = config.Stations.FindIndex(existing => existing.Id == station.Id);
            if (index >= 0)
            {
                config.Stations[index] = CloneStation(station);
            }
            else
            {
                config.Stations.Add(CloneStation(station));
            }
            Persist();
        }

        ConfigChanged?.Invoke();
    }

    public void SetStationSaveStreamTitles(Guid stationId, bool enabled)
    {
        var found = false;
        lock (gate)
        {
            var station = config.Stations.FirstOrDefault(value => value.Id == stationId);
            if (station is not null)
            {
                station.SaveStreamTitles = enabled;
                Persist();
                found = true;
            }
        }

        if (found)
        {
            Recorder.SetSaveStreamTitles(stationId, enabled);
            ConfigChanged?.Invoke();
        }
    }

    public void SetHourlyRecordingPlan(Guid stationId, HourlyRecordingMode mode, IEnumerable<int> hours)
    {
        var found = false;
        lock (gate)
        {
            var station = config.Stations.FirstOrDefault(value => value.Id == stationId);
            if (station is not null)
            {
                station.SetHourlyRecordingPlan(mode, hours);
                Persist();
                found = true;
            }
        }

        if (found)
        {
            ConfigChanged?.Invoke();
        }
    }

    public void DeleteStation(Guid stationId)
    {
        lock (gate)
        {
            config.Stations.RemoveAll(station => station.Id == stationId);
            config.Schedules.RemoveAll(schedule => schedule.StationId == stationId);
            Persist();
        }

        Recorder.Stop(stationId);
        Playback.Stop(stationId);
        ConfigChanged?.Invoke();
    }

    public void UpsertSchedule(ScheduleEntry schedule)
    {
        lock (gate)
        {
            var index = config.Schedules.FindIndex(existing => existing.Id == schedule.Id);
            if (index >= 0)
            {
                config.Schedules[index] = CloneSchedule(schedule);
            }
            else
            {
                config.Schedules.Add(CloneSchedule(schedule));
            }

            Persist();
        }

        ConfigChanged?.Invoke();
    }

    public void DeleteSchedule(Guid scheduleId)
    {
        lock (gate)
        {
            config.Schedules.RemoveAll(schedule => schedule.Id == scheduleId);
            Persist();
        }

        ConfigChanged?.Invoke();
    }

    public Task StartRecordingAsync(Guid stationId)
    {
        Station? station;
        AppSettings settings;

        lock (gate)
        {
            station = config.Stations.FirstOrDefault(value => value.Id == stationId) is { } found
                ? CloneStation(found)
                : null;
            settings = CloneConfig(config).Settings;
        }

        return station is null
            ? Task.CompletedTask
            : Recorder.StartAsync(station, settings, Paths, Logs);
    }

    public void StopRecording(Guid stationId)
    {
        Recorder.Stop(stationId);
    }

    public Task StartPlaybackAsync(Guid stationId)
    {
        Station? station;
        AppSettings settings;

        lock (gate)
        {
            station = config.Stations.FirstOrDefault(value => value.Id == stationId) is { } found
                ? CloneStation(found)
                : null;
            settings = CloneConfig(config).Settings;
        }

        return station is null
            ? Task.CompletedTask
            : Playback.StartAsync(station, settings, Paths, Logs);
    }

    public void StopPlayback(Guid stationId)
    {
        Playback.Stop(stationId);
    }

    public void Dispose()
    {
        Scheduler.Dispose();
        Recorder.Dispose();
        Playback.Dispose();
    }

    private void StartScheduler()
    {
        Scheduler.Start(
            schedulesProvider: GetSchedules,
            stationsProvider: GetStations,
            stationProvider: GetStation,
            languageProvider: () => GetSettings().Language,
            rootDirectoryProvider: () => Paths.RootDirectory,
            isRecording: Recorder.IsRecording,
            startRecordingAsync: StartRecordingAsync,
            stopRecording: StopRecording,
            logs: Logs);
    }

    private void Persist()
    {
        ConfigStore.Save(Paths, config);
    }

    private static AppConfig CloneConfig(AppConfig source)
    {
        return new AppConfig
        {
            Settings = new AppSettings
            {
                LaunchOnStartup = source.Settings.LaunchOnStartup,
                AlwaysOnTop = source.Settings.AlwaysOnTop,
                MinimizeToTray = source.Settings.MinimizeToTray,
                ConfirmOnExit = source.Settings.ConfirmOnExit,
                RestartOnCrash = source.Settings.RestartOnCrash,
                PreventSleep = source.Settings.PreventSleep,
                StartMinimized = source.Settings.StartMinimized,
                UseWindowsTaskScheduler = source.Settings.UseWindowsTaskScheduler,
                RemuxRawAacToM4A = source.Settings.RemuxRawAacToM4A,
                CreateCueSheets = source.Settings.CreateCueSheets,
                SplitRecordingsEnabled = source.Settings.SplitRecordingsEnabled,
                PlaybackDevice = source.Settings.PlaybackDevice,
                SplitHours = source.Settings.SplitHours,
                SplitMinutes = source.Settings.SplitMinutes,
                SplitSeconds = source.Settings.SplitSeconds,
                RecordingsFolder = source.Settings.RecordingsFolder,
                FileNameTemplate = source.Settings.FileNameTemplate,
                Language = source.Settings.Language,
            },
            Stations = source.Stations.Select(CloneStation).ToList(),
            Schedules = source.Schedules.Select(CloneSchedule).ToList(),
        };
    }

    private static Station CloneStation(Station station)
    {
        return new Station
        {
            Id = station.Id,
            Name = station.Name,
            Url = station.Url,
            SaveStreamTitles = station.SaveStreamTitles,
            HourlyRecordingMode = station.HourlyRecordingMode,
            HourlyRecordingHours = station.GetHourlyRecordingHours().ToList(),
            Credentials = station.Credentials is null
                ? null
                : new Credentials
                {
                    Username = station.Credentials.Username,
                    Password = station.Credentials.Password,
                },
        };
    }

    private static ScheduleEntry CloneSchedule(ScheduleEntry schedule)
    {
        return new ScheduleEntry
        {
            Id = schedule.Id,
            StationId = schedule.StationId,
            Enabled = schedule.Enabled,
            Days = schedule.GetDays().ToList(),
            StartHour = schedule.StartHour,
            StartMinute = schedule.StartMinute,
            StartSecond = schedule.StartSecond,
            EndHour = schedule.EndHour,
            EndMinute = schedule.EndMinute,
            EndSecond = schedule.EndSecond,
        };
    }
}
