using System.Globalization;
using StreamRecorder.Core.Models;

namespace StreamRecorder.Core.Localization;

public sealed class AppLocalizer
{
    private readonly Language language;

    private AppLocalizer(Language language)
    {
        this.language = language;
    }

    public Language Language => language;

    private bool IsPolish => language == Language.Polish;

    public static AppLocalizer For(Language language)
    {
        return new AppLocalizer(language);
    }

    public static void ApplyThreadCulture(Language language)
    {
        var culture = language == Language.Polish
            ? CultureInfo.GetCultureInfo("pl-PL")
            : CultureInfo.GetCultureInfo("en-US");

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public string AppTitle => "StreamRecorder";
    public string FileMenu => IsPolish ? "&Plik" : "&File";
    public string HelpMenu => IsPolish ? "P&omoc" : "&Help";
    public string OpenRecordingsFolder => IsPolish ? "&Otwórz folder nagrań" : "&Open recordings folder";
    public string OpenSettingsFolder => IsPolish ? "Otwórz folder &ustawień" : "Open se&ttings folder";
    public string Settings => IsPolish ? "&Ustawienia" : "&Settings";
    public string Exit => IsPolish ? "Za&kończ" : "E&xit";
    public string CheckForUpdates => IsPolish ? "&Sprawdź aktualizacje" : "&Check for updates";
    public string About => IsPolish ? "&O programie" : "&About";
    public string AddStation => IsPolish ? "&Dodaj stację" : "&Add station";
    public string StartRecording => IsPolish ? "&Rozpocznij nagrywanie" : "&Start recording";
    public string StopRecording => IsPolish ? "&Zatrzymaj nagrywanie" : "S&top recording";
    public string EditStation => IsPolish ? "&Edytuj stację" : "&Edit station";
    public string Schedules => IsPolish ? "&Harmonogram..." : "Sche&dules...";
    public string DeleteStation => IsPolish ? "&Usuń stację" : "&Delete station";
    public string ShowLog => IsPolish ? "Pokaż &log" : "Show &log";
    public string HideLog => IsPolish ? "Ukryj &log" : "Hide &log";
    public string Show => IsPolish ? "&Pokaż" : "&Show";
    public string StationColumn => IsPolish ? "Stacja" : "Station";
    public string UrlColumn => "URL";
    public string StatusColumn => IsPolish ? "Status" : "Status";
    public string FormatColumn => IsPolish ? "Format" : "Format";
    public string FileColumn => IsPolish ? "Plik" : "File";
    public string StationsAccessibleName => IsPolish ? "Stacje" : "Stations";
    public string StationsAccessibleDescription => IsPolish ? "Lista skonfigurowanych stacji." : "List of configured stations.";
    public string LogTitle => IsPolish ? "Log" : "Log";
    public string LogEntriesAccessibleName => IsPolish ? "Wpisy logu" : "Log entries";
    public string LogEntriesAccessibleDescription => IsPolish ? "Wpisy logu aplikacji." : "Application log entries.";
    public string Ok => "OK";
    public string Cancel => IsPolish ? "&Anuluj" : "&Cancel";
    public string Close => IsPolish ? "&Zamknij" : "&Close";
    public string SettingsTitle => IsPolish ? "Ustawienia" : "Settings";
    public string SettingsIntro => IsPolish
        ? "Te ustawienia kontrolują zachowanie przy starcie, folder nagrań, nazewnictwo plików i opcjonalny remuks AAC."
        : "These settings control startup behavior, the recording folder, file naming, and optional AAC remuxing.";
    public string GeneralGroup => IsPolish ? "Ogólne" : "General";
    public string RecordingGroup => IsPolish ? "Nagrywanie" : "Recording";
    public string OtherGroup => IsPolish ? "Inne" : "Other";
    public string LaunchOnStartup => IsPolish ? "Uruchamiaj aplikację wraz ze startem Windows" : "Launch application at Windows startup";
    public string AlwaysOnTop => IsPolish ? "Zawsze na wierzchu" : "Always on top";
    public string MinimizeToTray => IsPolish ? "Minimalizuj do zasobnika systemowego" : "Minimize to system tray";
    public string ConfirmOnExit => IsPolish ? "Pytaj o potwierdzenie przed zamknięciem" : "Ask for confirmation before exit";
    public string RestartOnCrash => IsPolish ? "Uruchom ponownie program po awarii" : "Restart program after a crash";
    public string PreventSleep => IsPolish ? "Zapobiegaj usypianiu komputera" : "Prevent the computer from sleeping";
    public string StartMinimized => IsPolish ? "Uruchamiaj zminimalizowany" : "Start minimized";
    public string RemuxRawAacToM4A => IsPolish ? "Remuksuj surowy AAC do M4A po nagraniu" : "Remux RAW AAC to M4A after recording";
    public string Browse => IsPolish ? "&Przeglądaj" : "B&rowse";
    public string RecordingFolderLabel => IsPolish ? "Folder &nagrań:" : "Recording &folder:";
    public string FileNameTemplateLabel => IsPolish ? "Sza&blon nazwy pliku:" : "File name &template:";
    public string FileNameTokens => IsPolish
        ? "Dostępne znaczniki: %t stacja, %r rok, %M miesiąc, %d dzień, %h godzina, %m minuta, %s sekunda"
        : "Available tokens: %t station, %r year, %M month, %d day, %h hour, %m minute, %s second";
    public string LanguageLabel => IsPolish ? "&Język:" : "&Language:";
    public string PolishLanguageName => "Polski";
    public string EnglishLanguageName => "English";
    public string RecordingFolderAccessibleName => IsPolish ? "Folder nagrań" : "Recording folder";
    public string FileNameTemplateAccessibleName => IsPolish ? "Szablon nazwy pliku" : "File name template";
    public string LanguageAccessibleName => IsPolish ? "Język" : "Language";
    public string StationDialogAddTitle => IsPolish ? "Dodaj stację" : "Add station";
    public string StationDialogEditTitle => IsPolish ? "Edytuj stację" : "Edit station";
    public string StationDialogIntro => IsPolish
        ? "Wprowadź poniżej szczegóły strumienia. Nazwa użytkownika i hasło są opcjonalne."
        : "Enter the stream details below. Username and password are optional.";
    public string StationInformationGroup => IsPolish ? "Informacje o stacji" : "Station information";
    public string OptionalCredentialsGroup => IsPolish ? "Opcjonalne dane logowania" : "Optional credentials";
    public string NameLabel => IsPolish ? "&Nazwa:" : "&Name:";
    public string UsernameLabel => IsPolish ? "Nazwa &użytkownika:" : "&Username:";
    public string PasswordLabel => IsPolish ? "&Hasło:" : "&Password:";
    public string StationNameAccessibleName => IsPolish ? "Nazwa stacji" : "Station name";
    public string StreamUrlAccessibleName => IsPolish ? "Adres strumienia" : "Stream URL";
    public string UsernameAccessibleName => IsPolish ? "Nazwa użytkownika" : "Username";
    public string PasswordAccessibleName => IsPolish ? "Hasło" : "Password";
    public string ValidationTitle => IsPolish ? "Walidacja" : "Validation";
    public string StationNameEmpty => IsPolish ? "Nazwa stacji nie może być pusta." : "Station name cannot be empty.";
    public string StreamUrlInvalid => IsPolish ? "Adres strumienia jest nieprawidłowy." : "The stream URL is not valid.";
    public string SchedulesTitle => IsPolish ? "Harmonogram" : "Schedules";
    public string ScheduleEntriesAccessibleName => IsPolish ? "Wpisy harmonogramu" : "Schedule entries";
    public string ScheduleEntriesAccessibleDescription => IsPolish ? "Lista wpisów harmonogramu dla wszystkich stacji." : "List of schedule entries for all stations.";
    public string Add => IsPolish ? "&Dodaj" : "&Add";
    public string Edit => IsPolish ? "&Edytuj" : "&Edit";
    public string Delete => IsPolish ? "&Usuń" : "&Delete";
    public string DayColumn => IsPolish ? "Dzień" : "Day";
    public string TimeColumn => IsPolish ? "Czas" : "Time";
    public string ActionColumn => IsPolish ? "Akcja" : "Action";
    public string EnabledColumn => IsPolish ? "Włączony" : "Enabled";
    public string MissingStation => IsPolish ? "(brak stacji)" : "(missing station)";
    public string Yes => IsPolish ? "Tak" : "Yes";
    public string No => IsPolish ? "Nie" : "No";
    public string AddStationBeforeSchedule => IsPolish
        ? "Dodaj co najmniej jedną stację, zanim utworzysz wpis harmonogramu."
        : "Add at least one station before creating a schedule entry.";
    public string DeleteSchedulePrompt => IsPolish ? "Usunąć ten wpis harmonogramu?" : "Delete this schedule entry?";
    public string DeleteScheduleTitle => IsPolish ? "Usuń harmonogram" : "Delete schedule";
    public string ScheduleEntryAddTitle => IsPolish ? "Dodaj wpis harmonogramu" : "Add schedule";
    public string ScheduleEntryEditTitle => IsPolish ? "Edytuj wpis harmonogramu" : "Edit schedule";
    public string ScheduleEntryRequiresStation => IsPolish
        ? "Do edycji harmonogramu wymagana jest co najmniej jedna stacja."
        : "At least one station is required to edit a schedule.";
    public string ScheduleEntryIntro => IsPolish
        ? "Wybierz stację, dzień, akcję i dokładny czas dla tego wpisu harmonogramu."
        : "Choose the station, day, action and exact time for this schedule entry.";
    public string ScheduleEntryGroup => IsPolish ? "Wpis harmonogramu" : "Schedule entry";
    public string StationLabel => IsPolish ? "&Stacja:" : "&Station:";
    public string DayLabel => IsPolish ? "&Dzień:" : "&Day:";
    public string ActionLabel => IsPolish ? "&Akcja:" : "&Action:";
    public string TimeLabel => IsPolish ? "&Czas:" : "&Time:";
    public string Enabled => IsPolish ? "&Włączony" : "&Enabled";
    public string DayAccessibleName => IsPolish ? "Dzień" : "Day";
    public string ActionAccessibleName => IsPolish ? "Akcja" : "Action";
    public string TimeAccessibleName => IsPolish ? "Czas" : "Time";
    public string CurrentlyRecording(int count) => IsPolish ? $"Aktualnie nagrywa: {count}" : $"Currently recording: {count}";
    public string DeleteStationPrompt(string stationName) => IsPolish ? $"Usunąć stację '{stationName}'?" : $"Delete station '{stationName}'?";
    public string DeleteStationTitle => IsPolish ? "Usuń stację" : "Delete station";
    public string NoNewerVersion => IsPolish ? "Nie ma nowszej wersji." : "No newer version is available.";
    public string UpdatesTitle => IsPolish ? "Aktualizacje" : "Updates";
    public string UpdateAvailableTitle => IsPolish ? "Dostępna aktualizacja" : "Update available";
    public string OpenReleasePagePrompt(string version) => IsPolish
        ? $"Dostępna wersja: {version}{Environment.NewLine}{Environment.NewLine}Otworzyć stronę wydania w przeglądarce?"
        : $"Available version: {version}{Environment.NewLine}{Environment.NewLine}Open the release page in your browser?";
    public string DownloadUpdatePrompt(string version, string assetName) => IsPolish
        ? $"Dostępna wersja: {version}{Environment.NewLine}Pakiet do pobrania: {assetName}{Environment.NewLine}{Environment.NewLine}Pobrać i zainstalować aktualizację teraz?"
        : $"Available version: {version}{Environment.NewLine}Downloadable asset: {assetName}{Environment.NewLine}{Environment.NewLine}Download and install the update now?";
    public string UpdateDownloadedAndClosing => IsPolish
        ? "Aktualizacja została pobrana. StreamRecorder zostanie teraz zamknięty i zainstaluje aktualizację."
        : "The update has been downloaded. StreamRecorder will now close and install the update.";
    public string AboutTitle => IsPolish ? "O programie" : "About";
    public string AboutText(string version) => IsPolish
        ? $"StreamRecorder {version}{Environment.NewLine}Powłoka WinForms rewrite"
        : $"StreamRecorder {version}{Environment.NewLine}WinForms rewrite shell";
    public string ConfirmClosePrompt => IsPolish ? "Czy na pewno chcesz zamknąć StreamRecorder?" : "Do you really want to close StreamRecorder?";
    public string ConfirmCloseTitle => IsPolish ? "Zamknij StreamRecorder" : "Close StreamRecorder";
    public string FailedSyncStartup(string message) => IsPolish ? $"Nie udało się zsynchronizować ustawienia autostartu: {message}" : $"Failed to sync startup setting: {message}";
    public string FailedSyncSleep(string message) => IsPolish ? $"Nie udało się zsynchronizować blokady usypiania: {message}" : $"Failed to sync sleep prevention: {message}";
    public string StateIdle => IsPolish ? "Bezczynny" : "Idle";
    public string StateConnecting => IsPolish ? "Łączenie" : "Connecting";
    public string StateReconnecting => IsPolish ? "Ponowne łączenie" : "Reconnecting";
    public string StateStopping => IsPolish ? "Zatrzymywanie" : "Stopping";
    public string StateStopped => IsPolish ? "Zatrzymano" : "Stopped";
    public string StateConnectionLostReconnecting => IsPolish ? "Połączenie utracone, łączę ponownie" : "Connection lost, reconnecting";
    public string StatePlaylistUnavailableRetrying => IsPolish ? "Playlista niedostępna, ponawiam próbę" : "Playlist unavailable, retrying";
    public string StateWaitingForHlsSegments => IsPolish ? "Oczekiwanie na segmenty HLS" : "Waiting for HLS segments";
    public string ErrorPrefix => IsPolish ? "Błąd: " : "Error: ";
    public string RecordingState(string formatDisplayName) => IsPolish ? $"Nagrywanie {formatDisplayName}" : $"Recording {formatDisplayName}";
    public string ConnectionFailed(string stationName, string message) => IsPolish ? $"Nie udało się połączyć ze stacją {stationName}: {message}" : $"Connection failed for {stationName}: {message}";
    public string StreamProducedNoDataRetrying(string stationName) => IsPolish ? $"Strumień {stationName} tymczasowo nie zwrócił danych, ponawiam próbę" : $"Stream {stationName} temporarily produced no data, retrying";
    public string RecordingStarted(string stationName, string outputPath, string formatName) => IsPolish ? $"Rozpoczęto nagrywanie: {stationName} -> {outputPath} ({formatName})" : $"Recording started: {stationName} -> {outputPath} ({formatName})";
    public string ConnectionEndedRetrying(string stationName) => IsPolish ? $"Połączenie ze stacją {stationName} zostało zakończone, ponawiam próbę" : $"Connection ended for {stationName}, retrying";
    public string ConnectionInterrupted(string stationName, string message) => IsPolish ? $"Połączenie ze stacją {stationName} zostało przerwane: {message}" : $"Connection interrupted for {stationName}: {message}";
    public string HlsPlaylistError(string stationName, string message) => IsPolish ? $"Błąd playlisty HLS dla {stationName}: {message}" : $"HLS playlist error for {stationName}: {message}";
    public string HlsRecordingStarted(string stationName, string outputPath, string formatName) => IsPolish ? $"Rozpoczęto nagrywanie HLS: {stationName} -> {outputPath} ({formatName})" : $"HLS recording started: {stationName} -> {outputPath} ({formatName})";
    public string HlsSegmentError(string stationName, string message) => IsPolish ? $"Błąd segmentu HLS dla {stationName}: {message}" : $"HLS segment error for {stationName}: {message}";
    public string RecordingStopped(string stationName) => IsPolish ? $"Nagrywanie zatrzymane: {stationName}" : $"Recording stopped: {stationName}";
    public string UnknownStreamFormat(string stationName, string sourceUrl, string mime, string bytesDescription) => IsPolish
        ? $"Nieznany format strumienia dla {stationName}. Nagrywanie będzie kontynuowane jako BIN. Źródło={sourceUrl}, content-type={mime}, pierwsze bajty={bytesDescription}"
        : $"Unknown stream format for {stationName}. Recording will continue as BIN. Source={sourceUrl}, content type={mime}, first bytes={bytesDescription}";
    public string ScheduleStartedRecording(string stationName) => IsPolish ? $"Harmonogram rozpoczął nagrywanie: {stationName}" : $"Schedule started recording: {stationName}";
    public string ScheduleStoppedRecording(string stationName) => IsPolish ? $"Harmonogram zatrzymał nagrywanie: {stationName}" : $"Schedule stopped recording: {stationName}";
    public string RemuxSkippingMp4BoxMissing => IsPolish ? "Pomijam remuks AAC do M4A: nie znaleziono MP4Box.exe" : "Skipping AAC to M4A remux: MP4Box.exe was not found";
    public string RemuxStarted(string outputPath) => IsPolish ? $"Remuks AAC do M4A: {outputPath}" : $"AAC to M4A remux: {outputPath}";
    public string RemuxFailed => IsPolish ? "Remuks AAC do M4A nie powiódł się" : "AAC to M4A remux failed";
    public string DayName(DayOfWeek day)
    {
        return day switch
        {
            DayOfWeek.Monday => IsPolish ? "Poniedziałek" : "Monday",
            DayOfWeek.Tuesday => IsPolish ? "Wtorek" : "Tuesday",
            DayOfWeek.Wednesday => IsPolish ? "Środa" : "Wednesday",
            DayOfWeek.Thursday => IsPolish ? "Czwartek" : "Thursday",
            DayOfWeek.Friday => IsPolish ? "Piątek" : "Friday",
            DayOfWeek.Saturday => IsPolish ? "Sobota" : "Saturday",
            _ => IsPolish ? "Niedziela" : "Sunday",
        };
    }

    public string ScheduleActionName(ScheduleAction action)
    {
        return action == Models.ScheduleAction.StartRecording
            ? (IsPolish ? "Rozpocznij nagrywanie" : "Start recording")
            : (IsPolish ? "Zatrzymaj nagrywanie" : "Stop recording");
    }

    public string FormatDisplayName(StreamFormat format)
    {
        return format switch
        {
            StreamFormat.Mp3 => "MP3",
            StreamFormat.AacRaw => "AAC",
            StreamFormat.Ogg => "OGG",
            StreamFormat.Flac => "FLAC",
            StreamFormat.Wma => "WMA",
            StreamFormat.Wav => "WAV",
            StreamFormat.MpegTs => "MPEG-TS",
            _ => IsPolish ? "Nieznany" : "Unknown",
        };
    }

    public string TranslateStateLabel(string? stateLabel)
    {
        if (string.IsNullOrWhiteSpace(stateLabel))
        {
            return StateIdle;
        }

        if (string.Equals(stateLabel, "Connecting", StringComparison.Ordinal))
        {
            return StateConnecting;
        }

        if (string.Equals(stateLabel, "Reconnecting", StringComparison.Ordinal))
        {
            return StateReconnecting;
        }

        if (string.Equals(stateLabel, "Waiting for reconnect", StringComparison.Ordinal))
        {
            return StateConnectionLostReconnecting;
        }

        if (string.Equals(stateLabel, "Waiting for playlist", StringComparison.Ordinal))
        {
            return StatePlaylistUnavailableRetrying;
        }

        if (string.Equals(stateLabel, "Waiting for HLS segments", StringComparison.Ordinal))
        {
            return StateWaitingForHlsSegments;
        }

        if (string.Equals(stateLabel, "Stopping", StringComparison.Ordinal))
        {
            return StateStopping;
        }

        if (string.Equals(stateLabel, "Stopped", StringComparison.Ordinal))
        {
            return StateStopped;
        }

        if (stateLabel.StartsWith("Recording ", StringComparison.Ordinal))
        {
            return RecordingState(stateLabel["Recording ".Length..]);
        }

        if (stateLabel.StartsWith("Error: ", StringComparison.Ordinal))
        {
            return ErrorPrefix + stateLabel["Error: ".Length..];
        }

        return stateLabel;
    }
}
