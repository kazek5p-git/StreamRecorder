using StreamRecorder.Core.Configuration;
using StreamRecorder.Core.Models;

namespace StreamRecorder.Core.Tests;

public sealed class StreamRecorderAppTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"sr_core_app_{Guid.NewGuid():N}");

    [Fact]
    public void SaveSettings_PersistsAcrossRestart()
    {
        var paths = AppPaths.Discover(Path.Combine(tempRoot, "streamrecorder.exe"));

        var settings = new AppSettings
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
            SplitHours = 1,
            SplitMinutes = 2,
            SplitSeconds = 3,
            RecordingsFolder = "Settings parity output",
            FileNameTemplate = "%t_session_%h-%m-%s",
            Language = LanguageCodes.English,
        };

        using (var app = new StreamRecorderApp("tests", paths))
        {
            app.SaveSettings(settings);
        }

        using var reloaded = new StreamRecorderApp("tests", paths);
        var loaded = reloaded.GetSettings();

        Assert.True(loaded.LaunchOnStartup);
        Assert.True(loaded.AlwaysOnTop);
        Assert.False(loaded.MinimizeToTray);
        Assert.False(loaded.ConfirmOnExit);
        Assert.True(loaded.RestartOnCrash);
        Assert.True(loaded.PreventSleep);
        Assert.True(loaded.StartMinimized);
        Assert.True(loaded.UseWindowsTaskScheduler);
        Assert.False(loaded.RemuxRawAacToM4A);
        Assert.True(loaded.SplitRecordingsEnabled);
        Assert.Equal(1, loaded.SplitHours);
        Assert.Equal(2, loaded.SplitMinutes);
        Assert.Equal(3, loaded.SplitSeconds);
        Assert.Equal("Settings parity output", loaded.RecordingsFolder);
        Assert.Equal("%t_session_%h-%m-%s", loaded.FileNameTemplate);
        Assert.Equal(LanguageCodes.English, loaded.Language);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, true);
        }
    }
}
