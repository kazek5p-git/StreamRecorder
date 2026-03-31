namespace StreamRecorder.Core.Configuration;

public sealed class AppPaths
{
    public required string RootDirectory { get; init; }

    public required string ConfigDirectory { get; init; }

    public required string RecordingsDirectory { get; init; }

    public required string ConfigFilePath { get; init; }

    public required string LogFilePath { get; init; }

    public static AppPaths Discover(string? executablePath = null)
    {
        executablePath ??= Environment.ProcessPath;
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
