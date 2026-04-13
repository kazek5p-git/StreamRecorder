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
                RemuxRawAacToM4A = persisted.Settings.RemuxRawAacToM4A,
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
                    Credentials = station.Credentials is null
                        ? null
                        : new Credentials
                        {
                            Username = station.Credentials.Username ?? string.Empty,
                            Password = station.Credentials.Password ?? string.Empty,
                        },
                })
                .ToList(),
            Schedules = (persisted.Schedules ?? [])
                .Select(static schedule => new ScheduleEntry
                {
                    Id = ParseGuidOrNew(schedule.Id),
                    StationId = ParseGuidOrNew(schedule.StationId),
                    Enabled = schedule.Enabled,
                    DayOfWeek = schedule.DayOfWeek,
                    Action = schedule.Action,
                    Hour = schedule.Hour,
                    Minute = schedule.Minute,
                    Second = schedule.Second,
                })
                .ToList(),
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
                RemuxRawAacToM4A = config.Settings.RemuxRawAacToM4A,
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
                    DayOfWeek = schedule.DayOfWeek,
                    Action = schedule.Action,
                    Hour = schedule.Hour,
                    Minute = schedule.Minute,
                    Second = schedule.Second,
                })
                .ToList(),
        };
    }

    private static Guid ParseGuidOrNew(string? value)
    {
        return Guid.TryParse(value, out var parsed) ? parsed : Guid.NewGuid();
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

        public bool RemuxRawAacToM4A { get; set; } = true;

        public string RecordingsFolder { get; set; } = AppDefaults.DefaultRecordingsFolder;

        public string FileNameTemplate { get; set; } = AppDefaults.DefaultFileNameTemplate;

        public string Language { get; set; } = LanguageCodes.Default;
    }

    private sealed class PersistedStation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("D");

        public string Name { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;

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

        public DayOfWeek DayOfWeek { get; set; }

        public ScheduleAction Action { get; set; } = ScheduleAction.StartRecording;

        public int Hour { get; set; }

        public int Minute { get; set; }

        public int Second { get; set; }
    }
}
