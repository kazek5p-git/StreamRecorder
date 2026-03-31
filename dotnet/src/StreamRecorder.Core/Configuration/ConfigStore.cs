using StreamRecorder.Core.Models;
using Tomlyn;

namespace StreamRecorder.Core.Configuration;

public static class ConfigStore
{
    public static AppConfig LoadOrCreate(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        paths.EnsureDirectories();

        if (!File.Exists(paths.ConfigFilePath))
        {
            var defaultConfig = CreateDefaultConfig();
            Save(paths, defaultConfig);
            return defaultConfig;
        }

        var contents = File.ReadAllText(paths.ConfigFilePath);
        var config = Toml.ToModel<AppConfig>(contents) ?? new AppConfig();

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
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(config);

        paths.EnsureDirectories();
        config.Settings ??= new AppSettings();
        config.Stations ??= [];
        config.Schedules ??= [];

        var toml = Toml.FromModel(config);
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
                Language = Language.Polish,
            },
            Stations = [],
            Schedules = [],
        };
    }
}
