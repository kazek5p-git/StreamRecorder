using StreamRecorder.Core.Configuration;
using StreamRecorder.Core.Tools;

namespace StreamRecorder.Core.Tests;

public sealed class Mp4BoxLocatorTests
{
    [Fact]
    public void EnumerateCandidates_IncludesPortableAndProgramFilesLocations()
    {
        var paths = CreatePaths(@"C:\Apps\StreamRecorder");
        var roots = new[] { @"C:\Program Files", @"C:\Program Files (x86)" };
        var directories = new Dictionary<string, string[]>
        {
            [@"C:\Program Files"] = [@"C:\Program Files\GPAC 2.4", @"C:\Program Files\NotGpac"],
            [@"C:\Program Files (x86)"] = [@"C:\Program Files (x86)\gpac nightly"],
        };

        var candidates = Mp4BoxLocator.EnumerateCandidates(
            paths,
            roots,
            root => directories.TryGetValue(root, out var values) ? values : []);

        Assert.Equal(Path.Combine(paths.RootDirectory, "Tools", "GPAC", "MP4Box.exe"), candidates[0]);
        Assert.Equal(Path.Combine(paths.RootDirectory, "Tools", "MP4Box.exe"), candidates[1]);
        Assert.Equal(Path.Combine(paths.RootDirectory, "MP4Box.exe"), candidates[2]);
        Assert.Contains(Path.Combine(@"C:\Program Files", "GPAC", "MP4Box.exe"), candidates);
        Assert.Contains(Path.Combine(@"C:\Program Files\GPAC 2.4", "MP4Box.exe"), candidates);
        Assert.Contains(Path.Combine(@"C:\Program Files\GPAC 2.4", "gpac", "MP4Box.exe"), candidates);
        Assert.Contains(Path.Combine(@"C:\Program Files (x86)\gpac nightly", "MP4Box.exe"), candidates);
        Assert.DoesNotContain(Path.Combine(@"C:\Program Files\NotGpac", "MP4Box.exe"), candidates);
    }

    [Fact]
    public void ResolveExecutablePath_PrefersPortableToolBeforeProgramFiles()
    {
        var paths = CreatePaths(@"C:\Apps\StreamRecorder");
        var portable = Path.Combine(paths.RootDirectory, "Tools", "GPAC", "MP4Box.exe");
        var shared = Path.Combine(@"C:\Program Files", "GPAC", "MP4Box.exe");

        var resolved = Mp4BoxLocator.ResolveExecutablePath(
            paths,
            [@"C:\Program Files"],
            _ => [],
            candidate => string.Equals(candidate, portable, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate, shared, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(portable, resolved);
    }

    private static AppPaths CreatePaths(string rootDirectory)
    {
        return new AppPaths
        {
            RootDirectory = rootDirectory,
            ConfigDirectory = Path.Combine(rootDirectory, "Config"),
            RecordingsDirectory = Path.Combine(rootDirectory, "My recordings"),
            ConfigFilePath = Path.Combine(rootDirectory, "Config", "app.toml"),
            LogFilePath = Path.Combine(rootDirectory, "Config", "streamrecorder.log"),
        };
    }
}
