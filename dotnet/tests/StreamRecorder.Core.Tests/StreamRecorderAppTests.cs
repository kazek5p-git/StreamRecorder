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
            RemuxRawAacToM4A = false,
            RecordingsFolder = "Settings parity output",
            FileNameTemplate = "%t_session_%h-%m-%s",
            Language = Language.English,
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
        Assert.False(loaded.RemuxRawAacToM4A);
        Assert.Equal("Settings parity output", loaded.RecordingsFolder);
        Assert.Equal("%t_session_%h-%m-%s", loaded.FileNameTemplate);
        Assert.Equal(Language.English, loaded.Language);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, true);
        }
    }
}
