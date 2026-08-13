using StreamRecorder.Core.Playback;

namespace StreamRecorder.Core.Tests;

public sealed class PlaybackServiceTests
{
    [Fact]
    public void PlaybackService_EnumeratesSystemDefaultAudioDevice()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var playback = new PlaybackService(repositoryRoot);

        var devices = playback.GetOutputDevices("System default test device");

        Assert.NotEmpty(devices);
        Assert.True(devices[0].IsSystemDefault);
        Assert.Equal("System default test device", devices[0].Name);
    }

    [Fact]
    public void PlaybackPackage_ContainsAacPluginForBothArchitectures()
    {
        var repositoryRoot = FindRepositoryRoot();

        Assert.True(File.Exists(Path.Combine(repositoryRoot, "third_party", "BASS", "x64", "bass_aac.dll")));
        Assert.True(File.Exists(Path.Combine(repositoryRoot, "third_party", "BASS", "x86", "bass_aac.dll")));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "third_party", "BASS", "x64", "bass.dll")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Nie znaleziono katalogu repozytorium z biblioteką BASS.");
    }
}
