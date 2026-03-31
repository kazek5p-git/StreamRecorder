using StreamRecorder.Core.Logging;

namespace StreamRecorder.Core.Tests;

public sealed class LogBusTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"sr_core_logs_{Guid.NewGuid():N}");

    [Fact]
    public void LogBus_LoadsExistingEntriesAndAppendsNewOnes()
    {
        Directory.CreateDirectory(tempRoot);
        var logPath = Path.Combine(tempRoot, "streamrecorder.log");
        File.WriteAllText(logPath, "[2026-03-31 08:00:00] Existing line" + Environment.NewLine);

        var bus = new LogBus(logPath);
        bus.Push("Next line");

        Assert.Equal(2, bus.Entries.Count);
        Assert.Contains("Existing line", bus.EntriesText);
        Assert.Contains("Next line", File.ReadAllText(logPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, true);
        }
    }
}
