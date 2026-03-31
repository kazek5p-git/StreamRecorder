namespace StreamRecorder.Core.Models;

public sealed class AppSettings
{
    public bool LaunchOnStartup { get; set; }

    public bool AlwaysOnTop { get; set; }

    public bool MinimizeToTray { get; set; } = true;

    public bool ConfirmOnExit { get; set; } = true;

    public bool RestartOnCrash { get; set; }

    public bool PreventSleep { get; set; }

    public bool StartMinimized { get; set; }

    public bool RemuxRawAacToM4A { get; set; } = true;

    public string RecordingsFolder { get; set; } = AppDefaults.DefaultRecordingsFolder;

    public string FileNameTemplate { get; set; } = AppDefaults.DefaultFileNameTemplate;

    public Language Language { get; set; } = Language.Polish;
}
