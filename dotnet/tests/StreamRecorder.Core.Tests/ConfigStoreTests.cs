using StreamRecorder.Core;
using StreamRecorder.Core.Configuration;
using StreamRecorder.Core.Models;

namespace StreamRecorder.Core.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"sr_core_config_{Guid.NewGuid():N}");

    [Fact]
    public void LoadOrCreate_CreatesDefaultConfigAndDirectories()
    {
        var paths = AppPaths.Discover(Path.Combine(tempRoot, "streamrecorder.exe"));

        var config = ConfigStore.LoadOrCreate(paths);

        Assert.True(Directory.Exists(paths.ConfigDirectory));
        Assert.True(Directory.Exists(paths.RecordingsDirectory));
        Assert.True(File.Exists(paths.ConfigFilePath));
        Assert.Equal(AppDefaults.DefaultRecordingsFolder, config.Settings.RecordingsFolder);
        Assert.Equal(AppDefaults.DefaultFileNameTemplate, config.Settings.FileNameTemplate);
        Assert.Empty(config.Stations);
    }

    [Fact]
    public void SaveAndReload_RoundTripsSettingsStationsAndSchedules()
    {
        var paths = AppPaths.Discover(Path.Combine(tempRoot, "streamrecorder.exe"));
        var stationId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();

        var config = new AppConfig
        {
            Settings = new AppSettings
            {
                LaunchOnStartup = true,
                AlwaysOnTop = true,
                MinimizeToTray = false,
                ConfirmOnExit = false,
                RestartOnCrash = true,
                PreventSleep = true,
                StartMinimized = true,
                RemuxRawAacToM4A = false,
                RecordingsFolder = "Parity recordings",
                FileNameTemplate = "%t_custom_%h-%m-%s",
                Language = LanguageCodes.English,
            },
            Stations =
            [
                new Station
                {
                    Id = stationId,
                    Name = "Parity FM",
                    Url = "https://example.invalid/stream.mp3",
                    Credentials = new Credentials
                    {
                        Username = "demo",
                        Password = "secret",
                    },
                },
            ],
            Schedules =
            [
                new ScheduleEntry
                {
                    Id = scheduleId,
                    StationId = stationId,
                    Enabled = true,
                    Days = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday],
                    Action = ScheduleAction.StopRecording,
                    Hour = 8,
                    Minute = 15,
                    Second = 30,
                },
            ],
        };

        ConfigStore.Save(paths, config);

        var reloaded = ConfigStore.LoadOrCreate(paths);

        Assert.True(reloaded.Settings.LaunchOnStartup);
        Assert.True(reloaded.Settings.AlwaysOnTop);
        Assert.False(reloaded.Settings.MinimizeToTray);
        Assert.False(reloaded.Settings.ConfirmOnExit);
        Assert.True(reloaded.Settings.RestartOnCrash);
        Assert.True(reloaded.Settings.PreventSleep);
        Assert.True(reloaded.Settings.StartMinimized);
        Assert.False(reloaded.Settings.RemuxRawAacToM4A);
        Assert.Equal("Parity recordings", reloaded.Settings.RecordingsFolder);
        Assert.Equal("%t_custom_%h-%m-%s", reloaded.Settings.FileNameTemplate);
        Assert.Equal(LanguageCodes.English, reloaded.Settings.Language);

        var station = Assert.Single(reloaded.Stations);
        Assert.Equal(stationId, station.Id);
        Assert.Equal("Parity FM", station.Name);
        Assert.Equal("https://example.invalid/stream.mp3", station.Url);
        Assert.NotNull(station.Credentials);
        Assert.Equal("demo", station.Credentials!.Username);
        Assert.Equal("secret", station.Credentials.Password);

        var schedule = Assert.Single(reloaded.Schedules);
        Assert.Equal(scheduleId, schedule.Id);
        Assert.Equal(stationId, schedule.StationId);
        Assert.True(schedule.Enabled);
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday }, schedule.GetDays());
        Assert.Equal(DayOfWeek.Monday, schedule.DayOfWeek);
        Assert.Equal(ScheduleAction.StopRecording, schedule.Action);
        Assert.Equal(8, schedule.Hour);
        Assert.Equal(15, schedule.Minute);
        Assert.Equal(30, schedule.Second);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, true);
        }
    }
}
