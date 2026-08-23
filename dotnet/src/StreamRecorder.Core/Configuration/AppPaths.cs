namespace StreamRecorder.Core.Configuration;

public sealed class AppPaths
{
    public string RootDirectory { get; set; } = string.Empty;

    public string ConfigDirectory { get; set; } = string.Empty;

    public string RecordingsDirectory { get; set; } = string.Empty;

    public string ConfigFilePath { get; set; } = string.Empty;

    public string LogFilePath { get; set; } = string.Empty;

    public string LegacyConfigFilePath { get; set; } = string.Empty;

    public bool UsesUserDataDirectory { get; set; }

    public static AppPaths Discover(
        string? executablePath = null,
        string? recordingsDirectoryOverride = null,
        bool? installedOverride = null)
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

        var legacyConfigDirectory = Path.Combine(rootDirectory, AppDefaults.ConfigDirectoryName);
        var usesUserDataDirectory = installedOverride ?? IsInstalledApplication(rootDirectory);
        var configDirectory = usesUserDataDirectory
            ? Path.Combine(GetUserDataRoot(), AppDefaults.ConfigDirectoryName)
            : legacyConfigDirectory;
        var recordingsDirectory = string.IsNullOrWhiteSpace(recordingsDirectoryOverride)
            ? AppDefaults.DefaultRecordingsFolder
            : recordingsDirectoryOverride!;

        return new AppPaths
        {
            RootDirectory = rootDirectory,
            ConfigDirectory = configDirectory,
            RecordingsDirectory = recordingsDirectory,
            ConfigFilePath = Path.Combine(configDirectory, AppDefaults.ConfigFileName),
            LogFilePath = Path.Combine(configDirectory, AppDefaults.LogFileName),
            LegacyConfigFilePath = Path.Combine(legacyConfigDirectory, AppDefaults.ConfigFileName),
            UsesUserDataDirectory = usesUserDataDirectory,
        };
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(RecordingsDirectory);
    }

    private static bool IsInstalledApplication(string rootDirectory)
    {
        var markerPath = Path.Combine(rootDirectory, AppDefaults.InstalledMarkerFileName);
        if (File.Exists(markerPath))
        {
            return true;
        }

        try
        {
            if (Directory.EnumerateFiles(rootDirectory, "unins*.exe", SearchOption.TopDirectoryOnly).Any())
            {
                return true;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return false;
        }

        var defaultInstallDirectory = Path.Combine(localAppData, "Programs", AppDefaults.UserDataDirectoryName);
        return PathsEqual(rootDirectory, defaultInstallDirectory);
    }

    private static string GetUserDataRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("Nie można ustalić katalogu LocalAppData bieżącego użytkownika.");
        }

        return Path.Combine(localAppData, AppDefaults.UserDataDirectoryName);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }
}
