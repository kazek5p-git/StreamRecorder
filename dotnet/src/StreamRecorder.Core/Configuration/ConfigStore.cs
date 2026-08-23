using StreamRecorder.Core.Models;
using Tomlyn;

namespace StreamRecorder.Core.Configuration;

public static class ConfigStore
{
    public static AppConfig LoadOrCreate(AppPaths paths)
    {
        if (paths is null)
        {
            throw new ArgumentNullException(nameof(paths));
        }

        paths.EnsureDirectories();

        if (!File.Exists(paths.ConfigFilePath))
        {
            var defaultConfig = CreateDefaultConfig();
            Save(paths, defaultConfig);
            return defaultConfig;
        }

        var contents = File.ReadAllText(paths.ConfigFilePath);
        var persisted = Toml.ToModel<PersistedAppConfig>(contents) ?? new PersistedAppConfig();
        var config = FromPersisted(persisted);

        config.Settings ??= new AppSettings();
        config.Stations ??= [];
        config.Schedules ??= [];

        if (string.IsNullOrWhiteSpace(config.Settings.RecordingsFolder))
        {
            config.Settings.RecordingsFolder = AppDefaults.DefaultRecordingsFolder;
        }

        if (string.IsNullOrWhiteSpace(config.Settings.FileNameTemplate))
        {
            config.Settings.FileNameTemplate = AppDefaults.DefaultFileNameTemplate;
        }

        return config;
    }

    public static void Save(AppPaths paths, AppConfig config)
    {
        if (paths is null)
        {
            throw new ArgumentNullException(nameof(paths));
        }
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        paths.EnsureDirectories();
        config.Settings ??= new AppSettings();
        config.Stations ??= [];
        config.Schedules ??= [];

        var toml = Toml.FromModel(ToPersisted(config));
        File.WriteAllText(paths.ConfigFilePath, toml);
    }

    private static AppConfig CreateDefaultConfig()
    {
        return new AppConfig
        {
            Settings = new AppSettings
            {
                RecordingsFolder = AppDefaults.DefaultRecordingsFolder,
                FileNameTemplate = AppDefaults.DefaultFileNameTemplate,
                Language = LanguageCodes.Default,
            },
            Stations = [],
            Schedules = [],
        };
    }

    private static AppConfig FromPersisted(PersistedAppConfig persisted)
    {
        var settings = persisted.Settings is null
            ? new AppSettings()
            : new AppSettings
            {
                LaunchOnStartup = persisted.Settings.LaunchOnStartup,
                AlwaysOnTop = persisted.Settings.AlwaysOnTop,
                MinimizeToTray = persisted.Settings.MinimizeToTray,
                ConfirmOnExit = persisted.Settings.ConfirmOnExit,
                RestartOnCrash = persisted.Settings.RestartOnCrash,
                PreventSleep = persisted.Settings.PreventSleep,
                StartMinimized = persisted.Settings.StartMinimized,
                UseWindowsTaskScheduler = persisted.Settings.UseWindowsTaskScheduler,
                RemuxRawAacToM4A = persisted.Settings.RemuxRawAacToM4A,
                CreateCueSheets = persisted.Settings.CreateCueSheets,
                SplitRecordingsEnabled = persisted.Settings.SplitRecordingsEnabled,
                PlaybackDevice = persisted.Settings.PlaybackDevice ?? string.Empty,
                SplitHours = persisted.Settings.SplitHours,
                SplitMinutes = persisted.Settings.SplitMinutes,
                SplitSeconds = persisted.Settings.SplitSeconds,
                RecordingsFolder = persisted.Settings.RecordingsFolder,
                FileNameTemplate = persisted.Settings.FileNameTemplate,
                Language = LanguageCodes.Normalize(persisted.Settings.Language),
            };

        return new AppConfig
        {
            Settings = settings,
            Stations = (persisted.Stations ?? [])
                .Select(static station => new Station
                {
                    Id = ParseGuidOrNew(station.Id),
                    Name = station.Name ?? string.Empty,
                    Url = station.Url ?? string.Empty,
                    SaveStreamTitles = station.SaveStreamTitles,
                    Credentials = station.Credentials is null
                        ? null
                        : new Credentials
                        {
                            Username = station.Credentials.Username ?? string.Empty,
                            Password = station.Credentials.Password ?? string.Empty,
                        },
                })
                .ToList(),
            Schedules = FromPersistedSchedules(persisted.Schedules),
        };
    }

    private static PersistedAppConfig ToPersisted(AppConfig config)
    {
        return new PersistedAppConfig
        {
            Settings = new PersistedAppSettings
            {
                LaunchOnStartup = config.Settings.LaunchOnStartup,
                AlwaysOnTop = config.Settings.AlwaysOnTop,
                MinimizeToTray = config.Settings.MinimizeToTray,
                ConfirmOnExit = config.Settings.ConfirmOnExit,
                RestartOnCrash = config.Settings.RestartOnCrash,
                PreventSleep = config.Settings.PreventSleep,
                StartMinimized = config.Settings.StartMinimized,
                UseWindowsTaskScheduler = config.Settings.UseWindowsTaskScheduler,
                RemuxRawAacToM4A = config.Settings.RemuxRawAacToM4A,
                CreateCueSheets = config.Settings.CreateCueSheets,
                SplitRecordingsEnabled = config.Settings.SplitRecordingsEnabled,
                PlaybackDevice = config.Settings.PlaybackDevice,
                SplitHours = config.Settings.SplitHours,
                SplitMinutes = config.Settings.SplitMinutes,
                SplitSeconds = config.Settings.SplitSeconds,
                RecordingsFolder = config.Settings.RecordingsFolder,
                FileNameTemplate = config.Settings.FileNameTemplate,
                Language = LanguageCodes.Normalize(config.Settings.Language),
            },
            Stations = config.Stations
                .Select(static station => new PersistedStation
                {
                    Id = station.Id.ToString("D"),
                    Name = station.Name,
                    Url = station.Url,
                    SaveStreamTitles = station.SaveStreamTitles,
                    Credentials = station.Credentials is null
                        ? null
                        : new PersistedCredentials
                        {
                            Username = station.Credentials.Username,
                            Password = station.Credentials.Password,
                        },
                })
                .ToList(),
            Schedules = config.Schedules
                .Select(static schedule => new PersistedScheduleEntry
                {
                    Id = schedule.Id.ToString("D"),
                    StationId = schedule.StationId.ToString("D"),
                    Enabled = schedule.Enabled,
                    Days = schedule.GetDays().ToList(),
                    StartHour = ClampHour(schedule.StartHour),
                    StartMinute = ClampMinuteOrSecond(schedule.StartMinute),
                    StartSecond = ClampMinuteOrSecond(schedule.StartSecond),
                    EndHour = ClampHour(schedule.EndHour),
                    EndMinute = ClampMinuteOrSecond(schedule.EndMinute),
                    EndSecond = ClampMinuteOrSecond(schedule.EndSecond),
                })
                .ToList(),
        };
    }

    private static Guid ParseGuidOrNew(string? value)
    {
        return Guid.TryParse(value, out var parsed) ? parsed : Guid.NewGuid();
    }

    private static List<DayOfWeek> NormalizeScheduleDays(List<DayOfWeek>? days, DayOfWeek? legacyDay)
    {
        var source = days is { Count: > 0 }
            ? days
            : legacyDay is { } day
                ? [day]
                : [DayOfWeek.Monday];

        return source
            .Distinct()
            .OrderBy(static day => day == DayOfWeek.Sunday ? 6 : (int)day - 1)
            .ToList();
    }

    private static List<ScheduleEntry> FromPersistedSchedules(List<PersistedScheduleEntry>? schedules)
    {
        if (schedules is null || schedules.Count == 0)
        {
            return [];
        }

        var result = new List<ScheduleEntry>();
        var legacyEvents = new List<LegacyScheduleEvent>();

        foreach (var schedule in schedules)
        {
            var id = ParseGuidOrNew(schedule.Id);
            var stationId = ParseGuidOrNew(schedule.StationId);
            var days = NormalizeScheduleDays(schedule.Days, schedule.DayOfWeek);

            if (HasWindowScheduleFields(schedule))
            {
                result.Add(new ScheduleEntry
                {
                    Id = id,
                    StationId = stationId,
                    Enabled = schedule.Enabled,
                    Days = days,
                    StartHour = ClampHour(schedule.StartHour ?? 0),
                    StartMinute = ClampMinuteOrSecond(schedule.StartMinute ?? 0),
                    StartSecond = ClampMinuteOrSecond(schedule.StartSecond ?? 0),
                    EndHour = ClampHour(schedule.EndHour ?? 1),
                    EndMinute = ClampMinuteOrSecond(schedule.EndMinute ?? 0),
                    EndSecond = ClampMinuteOrSecond(schedule.EndSecond ?? 0),
                });
                continue;
            }

            legacyEvents.Add(new LegacyScheduleEvent
            {
                Id = id,
                StationId = stationId,
                Enabled = schedule.Enabled,
                Days = days,
                Action = schedule.Action ?? ScheduleAction.StartRecording,
                Time = new TimeSpan(
                    ClampHour(schedule.Hour ?? 0),
                    ClampMinuteOrSecond(schedule.Minute ?? 0),
                    ClampMinuteOrSecond(schedule.Second ?? 0)),
            });
        }

        result.AddRange(PairLegacyScheduleEvents(legacyEvents));
        return result;
    }

    private static bool HasWindowScheduleFields(PersistedScheduleEntry schedule)
    {
        return schedule.StartHour.HasValue
            || schedule.StartMinute.HasValue
            || schedule.StartSecond.HasValue
            || schedule.EndHour.HasValue
            || schedule.EndMinute.HasValue
            || schedule.EndSecond.HasValue;
    }

    private static IEnumerable<ScheduleEntry> PairLegacyScheduleEvents(IReadOnlyList<LegacyScheduleEvent> events)
    {
        foreach (var group in events.GroupBy(static value => BuildLegacyScheduleGroupKey(value.StationId, value.Days)))
        {
            var starts = group
                .Where(static value => value.Action == ScheduleAction.StartRecording)
                .OrderBy(static value => value.Time)
                .ToList();
            var stops = group
                .Where(static value => value.Action == ScheduleAction.StopRecording)
                .OrderBy(static value => value.Time)
                .ToList();
            var usedStops = new HashSet<Guid>();

            foreach (var start in starts)
            {
                var stop = FindMatchingLegacyStop(start, stops, usedStops);
                if (stop is not null)
                {
                    usedStops.Add(stop.Id);
                }

                yield return CreateScheduleFromLegacyPair(
                    start.Id,
                    start.StationId,
                    stop is null ? start.Enabled : start.Enabled && stop.Enabled,
                    start.Days,
                    start.Time,
                    stop?.Time ?? AddLegacyDefaultDuration(start.Time));
            }

            foreach (var stop in stops.Where(stop => !usedStops.Contains(stop.Id)))
            {
                yield return CreateScheduleFromLegacyPair(
                    stop.Id,
                    stop.StationId,
                    stop.Enabled,
                    stop.Days,
                    SubtractLegacyDefaultDuration(stop.Time),
                    stop.Time);
            }
        }
    }

    private static LegacyScheduleEvent? FindMatchingLegacyStop(
        LegacyScheduleEvent start,
        IReadOnlyList<LegacyScheduleEvent> stops,
        HashSet<Guid> usedStops)
    {
        var sameDayStop = stops
            .Where(stop => !usedStops.Contains(stop.Id) && stop.Time > start.Time)
            .OrderBy(stop => stop.Time - start.Time)
            .FirstOrDefault();

        if (sameDayStop is not null)
        {
            return sameDayStop;
        }

        return stops
            .Where(stop => !usedStops.Contains(stop.Id))
            .OrderBy(stop => (TimeSpan.FromDays(1) - start.Time) + stop.Time)
            .FirstOrDefault();
    }

    private static ScheduleEntry CreateScheduleFromLegacyPair(
        Guid id,
        Guid stationId,
        bool enabled,
        IReadOnlyList<DayOfWeek> days,
        TimeSpan startTime,
        TimeSpan endTime)
    {
        var schedule = new ScheduleEntry
        {
            Id = id,
            StationId = stationId,
            Enabled = enabled,
            Days = days.ToList(),
        };
        schedule.SetStartTime(startTime);
        schedule.SetEndTime(endTime);
        return schedule;
    }

    private static TimeSpan AddLegacyDefaultDuration(TimeSpan time)
    {
        return NormalizeTimeOfDay(time + TimeSpan.FromHours(1));
    }

    private static TimeSpan SubtractLegacyDefaultDuration(TimeSpan time)
    {
        return NormalizeTimeOfDay(time - TimeSpan.FromHours(1));
    }

    private static TimeSpan NormalizeTimeOfDay(TimeSpan time)
    {
        while (time < TimeSpan.Zero)
        {
            time += TimeSpan.FromDays(1);
        }

        while (time >= TimeSpan.FromDays(1))
        {
            time -= TimeSpan.FromDays(1);
        }

        return time;
    }

    private static string BuildLegacyScheduleGroupKey(Guid stationId, IEnumerable<DayOfWeek> days)
    {
        return stationId.ToString("D") + "|" + string.Join(",", days.Select(static day => day.ToString()));
    }

    private static int ClampHour(int value)
    {
        return Math.Max(0, Math.Min(23, value));
    }

    private static int ClampMinuteOrSecond(int value)
    {
        return Math.Max(0, Math.Min(59, value));
    }

    private sealed class PersistedAppConfig
    {
        public PersistedAppSettings Settings { get; set; } = new();

        public List<PersistedStation> Stations { get; set; } = [];

        public List<PersistedScheduleEntry> Schedules { get; set; } = [];
    }

    private sealed class PersistedAppSettings
    {
        public bool LaunchOnStartup { get; set; }

        public bool AlwaysOnTop { get; set; }

        public bool MinimizeToTray { get; set; } = true;

        public bool ConfirmOnExit { get; set; } = true;

        public bool RestartOnCrash { get; set; }

        public bool PreventSleep { get; set; }

        public bool StartMinimized { get; set; }

        public bool UseWindowsTaskScheduler { get; set; }

        public bool RemuxRawAacToM4A { get; set; } = true;

        public bool CreateCueSheets { get; set; }

        public bool SplitRecordingsEnabled { get; set; }

        public string PlaybackDevice { get; set; } = string.Empty;

        public int SplitHours { get; set; }

        public int SplitMinutes { get; set; }

        public int SplitSeconds { get; set; }

        public string RecordingsFolder { get; set; } = AppDefaults.DefaultRecordingsFolder;

        public string FileNameTemplate { get; set; } = AppDefaults.DefaultFileNameTemplate;

        public string Language { get; set; } = LanguageCodes.Default;
    }

    private sealed class PersistedStation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("D");

        public string Name { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;

        public bool SaveStreamTitles { get; set; }

        public PersistedCredentials? Credentials { get; set; }
    }

    private sealed class PersistedCredentials
    {
        public string Username { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;
    }

    private sealed class PersistedScheduleEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("D");

        public string StationId { get; set; } = Guid.NewGuid().ToString("D");

        public bool Enabled { get; set; } = true;

        public List<DayOfWeek>? Days { get; set; }

        public DayOfWeek? DayOfWeek { get; set; }

        public int? StartHour { get; set; }

        public int? StartMinute { get; set; }

        public int? StartSecond { get; set; }

        public int? EndHour { get; set; }

        public int? EndMinute { get; set; }

        public int? EndSecond { get; set; }

        public ScheduleAction? Action { get; set; }

        public int? Hour { get; set; }

        public int? Minute { get; set; }

        public int? Second { get; set; }
    }

    private sealed class LegacyScheduleEvent
    {
        public Guid Id { get; set; }

        public Guid StationId { get; set; }

        public bool Enabled { get; set; }

        public IReadOnlyList<DayOfWeek> Days { get; set; } = [];

        public ScheduleAction Action { get; set; }

        public TimeSpan Time { get; set; }
    }
}
