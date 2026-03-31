using StreamRecorder.Core.Configuration;
using StreamRecorder.Core.Models;
using StreamRecorder.Core.Naming;

namespace StreamRecorder.Core.Tests;

public sealed class FileNameTemplateTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"sr_core_naming_{Guid.NewGuid():N}");

    [Fact]
    public void BuildOutputPath_SanitizesNameAndEnsuresUniqueness()
    {
        Directory.CreateDirectory(tempRoot);
        var paths = AppPaths.Discover(Path.Combine(tempRoot, "streamrecorder.exe"));
        var settings = new AppSettings();
        var station = Station.Create("Radio:Test/One", "http://example.invalid");
        var timestamp = new DateTimeOffset(2026, 3, 31, 8, 30, 15, TimeSpan.Zero);

        var firstPath = FileNameTemplate.BuildOutputPath(paths, settings, station, "mp3", timestamp);
        File.WriteAllText(firstPath, "existing");

        var secondPath = FileNameTemplate.BuildOutputPath(paths, settings, station, "mp3", timestamp);

        Assert.Contains("Radio_Test_One", Path.GetFileName(firstPath));
        Assert.NotEqual(firstPath, secondPath);
        Assert.EndsWith(".mp3", firstPath, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, true);
        }
    }
}
