namespace StreamRecorder.Core;

public static class AppDefaults
{
    public const string ConfigDirectoryName = "Config";
    public const string ConfigFileName = "app.toml";
    public const string LogFileName = "streamrecorder.log";
    public const string DefaultRecordingsFolderName = "StreamRecorder";
    public const string DefaultFileNameTemplate = "%t_%r-%M-%d_%h-%m-%s";
    public const string DefaultUpdateRepository = "kazek5p-git/StreamRecorder";

    public static string DefaultRecordingsFolder
    {
        get
        {
            var documentsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documentsDirectory))
            {
                documentsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            if (string.IsNullOrWhiteSpace(documentsDirectory))
            {
                throw new InvalidOperationException("Nie można ustalić katalogu Dokumenty bieżącego użytkownika.");
            }

            return Path.Combine(documentsDirectory, DefaultRecordingsFolderName);
        }
    }
}
