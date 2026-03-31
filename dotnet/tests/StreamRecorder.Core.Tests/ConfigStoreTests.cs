using StreamRecorder.Core;
using StreamRecorder.Core.Configuration;

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

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, true);
        }
    }
}
