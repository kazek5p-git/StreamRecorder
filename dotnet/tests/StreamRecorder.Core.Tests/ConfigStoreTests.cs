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
                UseWindowsTaskScheduler = true,
                RemuxRawAacToM4A = false,
                SplitRecordingsEnabled = true,
                SplitHours = 2,
                SplitMinutes = 15,
                SplitSeconds = 30,
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
                    StartHour = 8,
                    StartMinute = 15,
                    StartSecond = 30,
                    EndHour = 10,
                    EndMinute = 45,
                    EndSecond = 50,
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
        Assert.True(reloaded.Settings.UseWindowsTaskScheduler);
        Assert.False(reloaded.Settings.RemuxRawAacToM4A);
        Assert.True(reloaded.Settings.SplitRecordingsEnabled);
        Assert.Equal(2, reloaded.Settings.SplitHours);
        Assert.Equal(15, reloaded.Settings.SplitMinutes);
        Assert.Equal(30, reloaded.Settings.SplitSeconds);
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
        Assert.Equal(new TimeSpan(8, 15, 30), schedule.GetStartTime());
        Assert.Equal(new TimeSpan(10, 45, 50), schedule.GetEndTime());
    }

    [Fact]
    public void LoadOrCreate_MigratesLegacyStartStopSchedulesIntoRecordingWindow()
    {
        var paths = AppPaths.Discover(Path.Combine(tempRoot, "streamrecorder.exe"));
        paths.EnsureDirectories();
        var stationId = Guid.NewGuid();
        var startId = Guid.NewGuid();
        var stopId = Guid.NewGuid();
        var toml = $"""
            [[stations]]
            id = "{stationId:D}"
            name = "Legacy FM"
            url = "https://example.invalid/legacy.mp3"

            [[schedules]]
            id = "{startId:D}"
            station_id = "{stationId:D}"
            enabled = true
            days = ["Monday", "Tuesday"]
            action = "StartRecording"
            hour = 22
            minute = 30
            second = 15

            [[schedules]]
            id = "{stopId:D}"
            station_id = "{stationId:D}"
            enabled = true
            days = ["Monday", "Tuesday"]
            action = "StopRecording"
            hour = 2
            minute = 5
            second = 10
            """;
        File.WriteAllText(paths.ConfigFilePath, toml);

        var config = ConfigStore.LoadOrCreate(paths);

        var schedule = Assert.Single(config.Schedules);
        Assert.Equal(startId, schedule.Id);
        Assert.Equal(stationId, schedule.StationId);
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Tuesday }, schedule.GetDays());
        Assert.Equal(new TimeSpan(22, 30, 15), schedule.GetStartTime());
        Assert.Equal(new TimeSpan(2, 5, 10), schedule.GetEndTime());
        Assert.True(schedule.CrossesMidnight());
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, true);
        }
    }
}
