namespace StreamRecorder.Core.Configuration;

public sealed class AppPaths
{
    public string RootDirectory { get; set; } = string.Empty;

    public string ConfigDirectory { get; set; } = string.Empty;

    public string RecordingsDirectory { get; set; } = string.Empty;

    public string ConfigFilePath { get; set; } = string.Empty;

    public string LogFilePath { get; set; } = string.Empty;

    public static AppPaths Discover(string? executablePath = null)
    {
        executablePath ??= System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Unable to resolve the current executable path.");
        }

        var rootDirectory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new InvalidOperationException("Unable to resolve the application root directory.");
        }

        var configDirectory = Path.Combine(rootDirectory, AppDefaults.ConfigDirectoryName);
        var recordingsDirectory = Path.Combine(rootDirectory, AppDefaults.DefaultRecordingsFolder);

        return new AppPaths
        {
            RootDirectory = rootDirectory,
            ConfigDirectory = configDirectory,
            RecordingsDirectory = recordingsDirectory,
            ConfigFilePath = Path.Combine(configDirectory, AppDefaults.ConfigFileName),
            LogFilePath = Path.Combine(configDirectory, AppDefaults.LogFileName),
        };
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(RecordingsDirectory);
    }
}
