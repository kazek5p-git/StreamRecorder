using System.Globalization;
using System.Reflection;
using System.Text.Json;
using StreamRecorder.Core.Models;

namespace StreamRecorder.Core.Localization;

public sealed class AppLocalizer
{
    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);

    private readonly string languageCode;
    private readonly IReadOnlyDictionary<string, string> values;

    private AppLocalizer(string languageCode, IReadOnlyDictionary<string, string> values)
    {
        this.languageCode = LanguageCodes.Normalize(languageCode);
        this.values = values;
    }

    public string Language => languageCode;

    private bool IsPolish => string.Equals(languageCode, LanguageCodes.Polish, StringComparison.OrdinalIgnoreCase);

    public static AppLocalizer For(string languageCode)
    {
        return For(languageCode, null);
    }

    public static AppLocalizer For(string languageCode, string? rootDirectory)
    {
        var normalizedLanguageCode = LanguageCodes.Normalize(languageCode);
        var resolvedRoot = string.IsNullOrWhiteSpace(rootDirectory) ? AppContext.BaseDirectory : rootDirectory;
        var localePath = Path.Combine(GetLocalesDirectory(resolvedRoot), normalizedLanguageCode + ".json");
        return new AppLocalizer(normalizedLanguageCode, GetOrLoadValues(normalizedLanguageCode, localePath));
    }

    public static string GetLocalesDirectory(string? rootDirectory = null)
    {
        var resolvedRoot = string.IsNullOrWhiteSpace(rootDirectory) ? AppContext.BaseDirectory : rootDirectory;
        return Path.Combine(resolvedRoot, "locales");
    }

    public static IReadOnlyList<AvailableLanguage> GetAvailableLanguages(string? rootDirectory = null)
    {
        var localesDirectory = GetLocalesDirectory(rootDirectory);
        var languages = new List<AvailableLanguage>();

        if (!Directory.Exists(localesDirectory))
        {
            return new[]
            {
                new AvailableLanguage(LanguageCodes.Polish, "Polski"),
                new AvailableLanguage(LanguageCodes.English, "English"),
            };
        }

        foreach (var filePath in Directory.GetFiles(localesDirectory, "*.json"))
        {
            var code = LanguageCodes.Normalize(Path.GetFileNameWithoutExtension(filePath));
            var values = GetOrLoadValues(code, filePath);
            string? displayName;
            if (!values.TryGetValue("LanguageName", out displayName) || string.IsNullOrWhiteSpace(displayName))
            {
                displayName = code;
            }

            languages.Add(new AvailableLanguage(code, displayName));
        }

        if (languages.Count == 0)
        {
            languages.Add(new AvailableLanguage(LanguageCodes.Polish, "Polski"));
            languages.Add(new AvailableLanguage(LanguageCodes.English, "English"));
        }

        return languages
            .OrderBy(static language => language.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static void ApplyThreadCulture(string languageCode)
    {
        var normalizedLanguageCode = LanguageCodes.Normalize(languageCode);
        var culture = string.Equals(normalizedLanguageCode, LanguageCodes.Polish, StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.GetCultureInfo("pl-PL")
            : CultureInfo.GetCultureInfo("en-US");

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public string AppTitle => Text("AppTitle", "StreamRecorder");
    public string FileMenu => Text("FileMenu", IsPolish ? "&Plik" : "&File");
    public string HelpMenu => Text("HelpMenu", IsPolish ? "P&omoc" : "&Help");
    public string OpenRecordingsFolder => Text("OpenRecordingsFolder", IsPolish ? "&OtwĂłrz folder nagraĹ„" : "&Open recordings folder");
    public string OpenSettingsFolder => Text("OpenSettingsFolder", IsPolish ? "OtwĂłrz folder &ustawieĹ„" : "Open se&ttings folder");
    public string Settings => Text("Settings", IsPolish ? "&Ustawienia" : "&Settings");
    public string Exit => Text("Exit", IsPolish ? "Za&koĹ„cz" : "E&xit");
    public string CheckForUpdates => Text("CheckForUpdates", IsPolish ? "&SprawdĹş aktualizacje" : "&Check for updates");
    public string About => Text("About", IsPolish ? "&O programie" : "&About");
    public string AddStation => Text("AddStation", IsPolish ? "&Dodaj stacjÄ™" : "&Add station");
    public string StartRecording => Text("StartRecording", IsPolish ? "&Rozpocznij nagrywanie" : "&Start recording");
    public string StopRecording => Text("StopRecording", IsPolish ? "&Zatrzymaj nagrywanie" : "S&top recording");
    public string EditStation => Text("EditStation", IsPolish ? "&Edytuj stacjÄ™" : "&Edit station");
    public string Schedules => Text("Schedules", IsPolish ? "&Harmonogram..." : "Sche&dules...");
    public string DeleteStation => Text("DeleteStation", IsPolish ? "&UsuĹ„ stacjÄ™" : "&Delete station");
    public string ShowLog => Text("ShowLog", IsPolish ? "PokaĹĽ &log" : "Show &log");
    public string HideLog => Text("HideLog", IsPolish ? "Ukryj &log" : "Hide &log");
    public string Show => Text("Show", IsPolish ? "&PokaĹĽ" : "&Show");
    public string StationColumn => Text("StationColumn", IsPolish ? "Stacja" : "Station");
    public string UrlColumn => Text("UrlColumn", "URL");
    public string StatusColumn => Text("StatusColumn", IsPolish ? "Status" : "Status");
    public string FormatColumn => Text("FormatColumn", IsPolish ? "Format" : "Format");
    public string FileColumn => Text("FileColumn", IsPolish ? "Plik" : "File");
    public string StationsAccessibleName => Text("StationsAccessibleName", IsPolish ? "Stacje" : "Stations");
    public string StationsAccessibleDescription => Text("StationsAccessibleDescription", IsPolish ? "Lista skonfigurowanych stacji." : "List of configured stations.");
    public string NoStationsConfigured => Text("NoStationsConfigured", IsPolish ? "Brak dodanych stacji." : "No stations configured.");
    public string LogTitle => Text("LogTitle", "Log");
    public string LogEntriesAccessibleName => Text("LogEntriesAccessibleName", IsPolish ? "Wpisy logu" : "Log entries");
    public string LogEntriesAccessibleDescription => Text("LogEntriesAccessibleDescription", IsPolish ? "Wpisy logu aplikacji." : "Application log entries.");
    public string Ok => Text("Ok", "OK");
    public string Cancel => Text("Cancel", IsPolish ? "&Anuluj" : "&Cancel");
    public string Close => Text("Close", IsPolish ? "&Zamknij" : "&Close");
    public string SettingsTitle => Text("SettingsTitle", IsPolish ? "Ustawienia" : "Settings");
    public string SettingsIntro => Text("SettingsIntro", IsPolish
        ? "Te ustawienia kontrolujÄ… zachowanie przy starcie, folder nagraĹ„, nazewnictwo plikĂłw i opcjonalny remuks AAC."
        : "These settings control startup behavior, the recording folder, file naming, and optional AAC remuxing.");
    public string GeneralGroup => Text("GeneralGroup", IsPolish ? "OgĂłlne" : "General");
    public string RecordingGroup => Text("RecordingGroup", IsPolish ? "Nagrywanie" : "Recording");
    public string OtherGroup => Text("OtherGroup", IsPolish ? "Inne" : "Other");
    public string LaunchOnStartup => Text("LaunchOnStartup", IsPolish ? "Uruchamiaj aplikacjÄ™ wraz ze startem Windows" : "Launch application at Windows startup");
    public string AlwaysOnTop => Text("AlwaysOnTop", IsPolish ? "Zawsze na wierzchu" : "Always on top");
    public string MinimizeToTray => Text("MinimizeToTray", IsPolish ? "Minimalizuj do zasobnika systemowego" : "Minimize to system tray");
    public string ConfirmOnExit => Text("ConfirmOnExit", IsPolish ? "Pytaj o potwierdzenie przed zamkniÄ™ciem" : "Ask for confirmation before exit");
    public string RestartOnCrash => Text("RestartOnCrash", IsPolish ? "Uruchom ponownie program po awarii" : "Restart program after a crash");
    public string PreventSleep => Text("PreventSleep", IsPolish ? "Zapobiegaj usypianiu komputera" : "Prevent the computer from sleeping");
    public string StartMinimized => Text("StartMinimized", IsPolish ? "Uruchamiaj zminimalizowany" : "Start minimized");
    public string RemuxRawAacToM4A => Text("RemuxRawAacToM4A", IsPolish ? "Remuksuj surowy AAC do M4A po nagraniu" : "Remux RAW AAC to M4A after recording");
    public string Browse => Text("Browse", IsPolish ? "&PrzeglÄ…daj" : "B&rowse");
    public string RecordingFolderLabel => Text("RecordingFolderLabel", IsPolish ? "Folder &nagraĹ„:" : "Recording &folder:");
    public string FileNameTemplateLabel => Text("FileNameTemplateLabel", IsPolish ? "Sza&blon nazwy pliku:" : "File name &template:");
    public string FileNameTokens => Text("FileNameTokens", IsPolish
        ? "DostÄ™pne znaczniki: %t stacja, %r rok, %M miesiÄ…c, %d dzieĹ„, %h godzina, %m minuta, %s sekunda"
        : "Available tokens: %t station, %r year, %M month, %d day, %h hour, %m minute, %s second");
    public string LanguageLabel => Text("LanguageLabel", IsPolish ? "&JÄ™zyk:" : "&Language:");
    public string PolishLanguageName => Text("PolishLanguageName", "Polski");
    public string EnglishLanguageName => Text("EnglishLanguageName", "English");
    public string RecordingFolderAccessibleName => Text("RecordingFolderAccessibleName", IsPolish ? "Folder nagraĹ„" : "Recording folder");
    public string FileNameTemplateAccessibleName => Text("FileNameTemplateAccessibleName", IsPolish ? "Szablon nazwy pliku" : "File name template");
    public string LanguageAccessibleName => Text("LanguageAccessibleName", IsPolish ? "JÄ™zyk" : "Language");
    public string StationDialogAddTitle => Text("StationDialogAddTitle", IsPolish ? "Dodaj stacjÄ™" : "Add station");
    public string StationDialogEditTitle => Text("StationDialogEditTitle", IsPolish ? "Edytuj stacjÄ™" : "Edit station");
    public string StationDialogIntro => Text("StationDialogIntro", IsPolish
        ? "WprowadĹş poniĹĽej szczegĂłĹ‚y strumienia. Nazwa uĹĽytkownika i hasĹ‚o sÄ… opcjonalne."
        : "Enter the stream details below. Username and password are optional.");
    public string StationInformationGroup => Text("StationInformationGroup", IsPolish ? "Informacje o stacji" : "Station information");
    public string OptionalCredentialsGroup => Text("OptionalCredentialsGroup", IsPolish ? "Opcjonalne dane logowania" : "Optional credentials");
    public string NameLabel => Text("NameLabel", IsPolish ? "&Nazwa:" : "&Name:");
    public string UsernameLabel => Text("UsernameLabel", IsPolish ? "Nazwa &uĹĽytkownika:" : "&Username:");
    public string PasswordLabel => Text("PasswordLabel", IsPolish ? "&HasĹ‚o:" : "&Password:");
    public string StationNameAccessibleName => Text("StationNameAccessibleName", IsPolish ? "Nazwa stacji" : "Station name");
    public string StreamUrlAccessibleName => Text("StreamUrlAccessibleName", IsPolish ? "Adres strumienia" : "Stream URL");
    public string UsernameAccessibleName => Text("UsernameAccessibleName", IsPolish ? "Nazwa uĹĽytkownika" : "Username");
    public string PasswordAccessibleName => Text("PasswordAccessibleName", IsPolish ? "HasĹ‚o" : "Password");
    public string ValidationTitle => Text("ValidationTitle", IsPolish ? "Walidacja" : "Validation");
    public string StationNameEmpty => Text("StationNameEmpty", IsPolish ? "Nazwa stacji nie moĹĽe byÄ‡ pusta." : "Station name cannot be empty.");
    public string StreamUrlInvalid => Text("StreamUrlInvalid", IsPolish ? "Adres strumienia jest nieprawidĹ‚owy." : "The stream URL is not valid.");
    public string SchedulesTitle => Text("SchedulesTitle", IsPolish ? "Harmonogram" : "Schedules");
    public string ScheduleEntriesAccessibleName => Text("ScheduleEntriesAccessibleName", IsPolish ? "Wpisy harmonogramu" : "Schedule entries");
    public string ScheduleEntriesAccessibleDescription => Text("ScheduleEntriesAccessibleDescription", IsPolish ? "Lista wpisĂłw harmonogramu dla wszystkich stacji." : "List of schedule entries for all stations.");
    public string Add => Text("Add", IsPolish ? "&Dodaj" : "&Add");
    public string Edit => Text("Edit", IsPolish ? "&Edytuj" : "&Edit");
    public string Delete => Text("Delete", IsPolish ? "&UsuĹ„" : "&Delete");
    public string DayColumn => Text("DayColumn", IsPolish ? "DzieĹ„" : "Day");
    public string DaysColumn => Text("DaysColumn", IsPolish ? "Dni" : "Days");
    public string TimeColumn => Text("TimeColumn", IsPolish ? "Czas" : "Time");
    public string ActionColumn => Text("ActionColumn", IsPolish ? "Akcja" : "Action");
    public string EnabledColumn => Text("EnabledColumn", IsPolish ? "WĹ‚Ä…czony" : "Enabled");
    public string MissingStation => Text("MissingStation", IsPolish ? "(brak stacji)" : "(missing station)");
    public string Yes => Text("Yes", IsPolish ? "Tak" : "Yes");
    public string No => Text("No", IsPolish ? "Nie" : "No");
    public string AddStationBeforeSchedule => Text("AddStationBeforeSchedule", IsPolish
        ? "Dodaj co najmniej jednÄ… stacjÄ™, zanim utworzysz wpis harmonogramu."
        : "Add at least one station before creating a schedule entry.");
    public string DeleteSchedulePrompt => Text("DeleteSchedulePrompt", IsPolish ? "UsunÄ…Ä‡ ten wpis harmonogramu?" : "Delete this schedule entry?");
    public string DeleteScheduleTitle => Text("DeleteScheduleTitle", IsPolish ? "UsuĹ„ harmonogram" : "Delete schedule");
    public string ScheduleEntryAddTitle => Text("ScheduleEntryAddTitle", IsPolish ? "Dodaj wpis harmonogramu" : "Add schedule");
    public string ScheduleEntryEditTitle => Text("ScheduleEntryEditTitle", IsPolish ? "Edytuj wpis harmonogramu" : "Edit schedule");
    public string ScheduleEntryRequiresStation => Text("ScheduleEntryRequiresStation", IsPolish
        ? "Do edycji harmonogramu wymagana jest co najmniej jedna stacja."
        : "At least one station is required to edit a schedule.");
    public string ScheduleEntryIntro => Text("ScheduleEntryIntro", IsPolish
        ? "Wybierz stacjÄ™, dzieĹ„, akcjÄ™ i dokĹ‚adny czas dla tego wpisu harmonogramu."
        : "Choose the station, day, action and exact time for this schedule entry.");
    public string ScheduleEntryGroup => Text("ScheduleEntryGroup", IsPolish ? "Wpis harmonogramu" : "Schedule entry");
    public string StationLabel => Text("StationLabel", IsPolish ? "&Stacja:" : "&Station:");
    public string DayLabel => Text("DayLabel", IsPolish ? "&DzieĹ„:" : "&Day:");
    public string DaysLabel => Text("DaysLabel", IsPolish ? "&Dni:" : "&Days:");
    public string ActionLabel => Text("ActionLabel", IsPolish ? "&Akcja:" : "&Action:");
    public string TimeLabel => Text("TimeLabel", IsPolish ? "&Czas:" : "&Time:");
    public string Enabled => Text("Enabled", IsPolish ? "&WĹ‚Ä…czony" : "&Enabled");
    public string DayAccessibleName => Text("DayAccessibleName", IsPolish ? "DzieĹ„" : "Day");
    public string DaysAccessibleName => Text("DaysAccessibleName", IsPolish ? "Dni" : "Days");
    public string ActionAccessibleName => Text("ActionAccessibleName", IsPolish ? "Akcja" : "Action");
    public string TimeAccessibleName => Text("TimeAccessibleName", IsPolish ? "Czas" : "Time");
    public string ScheduleEntryRequiresDay => Text("ScheduleEntryRequiresDay", IsPolish ? "Wybierz co najmniej jeden dzieĹ„." : "Select at least one day.");
    public string DeleteStationTitle => Text("DeleteStationTitle", IsPolish ? "UsuĹ„ stacjÄ™" : "Delete station");
    public string NoNewerVersion => Text("NoNewerVersion", IsPolish ? "Nie ma nowszej wersji." : "No newer version is available.");
    public string UpdatesTitle => Text("UpdatesTitle", IsPolish ? "Aktualizacje" : "Updates");
    public string UpdateAvailableTitle => Text("UpdateAvailableTitle", IsPolish ? "DostÄ™pna aktualizacja" : "Update available");
    public string AboutTitle => Text("AboutTitle", IsPolish ? "O programie" : "About");
    public string ConfirmCloseTitle => Text("ConfirmCloseTitle", IsPolish ? "Zamknij StreamRecorder" : "Close StreamRecorder");
    public string StateIdle => Text("StateIdle", IsPolish ? "Bezczynny" : "Idle");
    public string StateConnecting => Text("StateConnecting", IsPolish ? "ĹÄ…czenie" : "Connecting");
    public string StateReconnecting => Text("StateReconnecting", IsPolish ? "Ponowne Ĺ‚Ä…czenie" : "Reconnecting");
    public string StateStopping => Text("StateStopping", IsPolish ? "Zatrzymywanie" : "Stopping");
    public string StateStopped => Text("StateStopped", IsPolish ? "Zatrzymano" : "Stopped");
    public string StateConnectionLostReconnecting => Text("StateConnectionLostReconnecting", IsPolish ? "PoĹ‚Ä…czenie utracone, Ĺ‚Ä…czÄ™ ponownie" : "Connection lost, reconnecting");
    public string StatePlaylistUnavailableRetrying => Text("StatePlaylistUnavailableRetrying", IsPolish ? "Playlista niedostÄ™pna, ponawiam prĂłbÄ™" : "Playlist unavailable, retrying");
    public string StateWaitingForHlsSegments => Text("StateWaitingForHlsSegments", IsPolish ? "Oczekiwanie na segmenty HLS" : "Waiting for HLS segments");
    public string ErrorPrefix => Text("ErrorPrefix", IsPolish ? "BĹ‚Ä…d: " : "Error: ");
    public string RemuxSkippingMp4BoxMissing => Text("RemuxSkippingMp4BoxMissing", IsPolish ? "Pomijam remuks AAC do M4A: nie znaleziono MP4Box.exe" : "Skipping AAC to M4A remux: MP4Box.exe was not found");
    public string RemuxFailed => Text("RemuxFailed", IsPolish ? "Remuks AAC do M4A nie powiĂłdĹ‚ siÄ™" : "AAC to M4A remux failed");

    public string CurrentlyRecording(int count) => Format("CurrentlyRecording", IsPolish ? "Aktualnie nagrywa: {0}" : "Currently recording: {0}", count);
    public string DeleteStationPrompt(string stationName) => Format("DeleteStationPrompt", IsPolish ? "UsunÄ…Ä‡ stacjÄ™ '{0}'?" : "Delete station '{0}'?", stationName);
    public string OpenReleasePagePrompt(string version) => Format("OpenReleasePagePrompt", IsPolish
        ? "DostÄ™pna wersja: {0}\n\nOtworzyÄ‡ stronÄ™ wydania w przeglÄ…darce?"
        : "Available version: {0}\n\nOpen the release page in your browser?", version);
    public string DownloadUpdatePrompt(string version, string assetName) => Format("DownloadUpdatePrompt", IsPolish
        ? "DostÄ™pna wersja: {0}\nPakiet do pobrania: {1}\n\nPobraÄ‡ i zainstalowaÄ‡ aktualizacjÄ™ teraz?"
        : "Available version: {0}\nDownloadable asset: {1}\n\nDownload and install the update now?", version, assetName);
    public string UpdateDownloadedAndClosing => Text("UpdateDownloadedAndClosing", IsPolish
        ? "Aktualizacja zostaĹ‚a pobrana. StreamRecorder zostanie teraz zamkniÄ™ty i zainstaluje aktualizacjÄ™."
        : "The update has been downloaded. StreamRecorder will now close and install the update.");
    public string AboutText(string version) => Format("AboutText", IsPolish ? "StreamRecorder {0}\nPowĹ‚oka WinForms rewrite" : "StreamRecorder {0}\nWinForms rewrite shell", version);
    public string ConfirmClosePrompt => Text("ConfirmClosePrompt", IsPolish ? "Czy na pewno chcesz zamknÄ…Ä‡ StreamRecorder?" : "Do you really want to close StreamRecorder?");
    public string FailedSyncStartup(string message) => Format("FailedSyncStartup", IsPolish ? "Nie udaĹ‚o siÄ™ zsynchronizowaÄ‡ ustawienia autostartu: {0}" : "Failed to sync startup setting: {0}", message);
    public string FailedSyncSleep(string message) => Format("FailedSyncSleep", IsPolish ? "Nie udaĹ‚o siÄ™ zsynchronizowaÄ‡ blokady usypiania: {0}" : "Failed to sync sleep prevention: {0}", message);
    public string RecordingState(string formatDisplayName) => Format("RecordingState", IsPolish ? "Nagrywanie {0}" : "Recording {0}", formatDisplayName);
    public string ConnectionFailed(string stationName, string message) => Format("ConnectionFailed", IsPolish ? "Nie udaĹ‚o siÄ™ poĹ‚Ä…czyÄ‡ ze stacjÄ… {0}: {1}" : "Connection failed for {0}: {1}", stationName, message);
    public string StreamProducedNoDataRetrying(string stationName) => Format("StreamProducedNoDataRetrying", IsPolish ? "StrumieĹ„ {0} tymczasowo nie zwrĂłciĹ‚ danych, ponawiam prĂłbÄ™" : "Stream {0} temporarily produced no data, retrying", stationName);
    public string RecordingStarted(string stationName, string outputPath, string formatName) => Format("RecordingStarted", IsPolish ? "RozpoczÄ™to nagrywanie: {0} -> {1} ({2})" : "Recording started: {0} -> {1} ({2})", stationName, outputPath, formatName);
    public string ConnectionEndedRetrying(string stationName) => Format("ConnectionEndedRetrying", IsPolish ? "PoĹ‚Ä…czenie ze stacjÄ… {0} zostaĹ‚o zakoĹ„czone, ponawiam prĂłbÄ™" : "Connection ended for {0}, retrying", stationName);
    public string ConnectionInterrupted(string stationName, string message) => Format("ConnectionInterrupted", IsPolish ? "PoĹ‚Ä…czenie ze stacjÄ… {0} zostaĹ‚o przerwane: {1}" : "Connection interrupted for {0}: {1}", stationName, message);
    public string HlsPlaylistError(string stationName, string message) => Format("HlsPlaylistError", IsPolish ? "BĹ‚Ä…d playlisty HLS dla {0}: {1}" : "HLS playlist error for {0}: {1}", stationName, message);
    public string HlsRecordingStarted(string stationName, string outputPath, string formatName) => Format("HlsRecordingStarted", IsPolish ? "RozpoczÄ™to nagrywanie HLS: {0} -> {1} ({2})" : "HLS recording started: {0} -> {1} ({2})", stationName, outputPath, formatName);
    public string HlsSegmentError(string stationName, string message) => Format("HlsSegmentError", IsPolish ? "BĹ‚Ä…d segmentu HLS dla {0}: {1}" : "HLS segment error for {0}: {1}", stationName, message);
    public string RecordingStopped(string stationName) => Format("RecordingStopped", IsPolish ? "Nagrywanie zatrzymane: {0}" : "Recording stopped: {0}", stationName);
    public string UnknownStreamFormat(string stationName, string sourceUrl, string mime, string bytesDescription) => Format("UnknownStreamFormat", IsPolish
        ? "Nieznany format strumienia dla {0}. Nagrywanie bÄ™dzie kontynuowane jako BIN. ĹąrĂłdĹ‚o={1}, content-type={2}, pierwsze bajty={3}"
        : "Unknown stream format for {0}. Recording will continue as BIN. Source={1}, content type={2}, first bytes={3}", stationName, sourceUrl, mime, bytesDescription);
    public string ScheduleStartedRecording(string stationName) => Format("ScheduleStartedRecording", IsPolish ? "Harmonogram rozpoczÄ…Ĺ‚ nagrywanie: {0}" : "Schedule started recording: {0}", stationName);
    public string ScheduleStoppedRecording(string stationName) => Format("ScheduleStoppedRecording", IsPolish ? "Harmonogram zatrzymaĹ‚ nagrywanie: {0}" : "Schedule stopped recording: {0}", stationName);
    public string RemuxStarted(string outputPath) => Format("RemuxStarted", IsPolish ? "Remuks AAC do M4A: {0}" : "AAC to M4A remux: {0}", outputPath);

    public string DayName(DayOfWeek day)
    {
        switch (day)
        {
            case DayOfWeek.Monday:
                return Text("DayName.Monday", IsPolish ? "PoniedziaĹ‚ek" : "Monday");
            case DayOfWeek.Tuesday:
                return Text("DayName.Tuesday", IsPolish ? "Wtorek" : "Tuesday");
            case DayOfWeek.Wednesday:
                return Text("DayName.Wednesday", IsPolish ? "Ĺšroda" : "Wednesday");
            case DayOfWeek.Thursday:
                return Text("DayName.Thursday", IsPolish ? "Czwartek" : "Thursday");
            case DayOfWeek.Friday:
                return Text("DayName.Friday", IsPolish ? "PiÄ…tek" : "Friday");
            case DayOfWeek.Saturday:
                return Text("DayName.Saturday", IsPolish ? "Sobota" : "Saturday");
            default:
                return Text("DayName.Sunday", IsPolish ? "Niedziela" : "Sunday");
        }
    }

    public string ScheduleActionName(ScheduleAction action)
    {
        return action == Models.ScheduleAction.StartRecording
            ? Text("ScheduleActionName.StartRecording", IsPolish ? "Rozpocznij nagrywanie" : "Start recording")
            : Text("ScheduleActionName.StopRecording", IsPolish ? "Zatrzymaj nagrywanie" : "Stop recording");
    }

    public string FormatDisplayName(StreamFormat format)
    {
        switch (format)
        {
            case StreamFormat.Mp3:
                return Text("FormatDisplayName.Mp3", "MP3");
            case StreamFormat.AacRaw:
                return Text("FormatDisplayName.AacRaw", "AAC");
            case StreamFormat.Ogg:
                return Text("FormatDisplayName.Ogg", "OGG");
            case StreamFormat.Flac:
                return Text("FormatDisplayName.Flac", "FLAC");
            case StreamFormat.Wma:
                return Text("FormatDisplayName.Wma", "WMA");
            case StreamFormat.Wav:
                return Text("FormatDisplayName.Wav", "WAV");
            case StreamFormat.MpegTs:
                return Text("FormatDisplayName.MpegTs", "MPEG-TS");
            default:
                return Text("FormatDisplayName.Unknown", IsPolish ? "Nieznany" : "Unknown");
        }
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
            return RecordingState(stateLabel.Substring("Recording ".Length));
        }

        if (stateLabel.StartsWith("Error: ", StringComparison.Ordinal))
        {
            return ErrorPrefix + stateLabel.Substring("Error: ".Length);
        }

        return stateLabel;
    }

    private string Text(string key, string fallback)
    {
        string? value;
        if (values.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return fallback;
    }

    private string Format(string key, string fallback, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, Text(key, fallback), args);
    }

    private static IReadOnlyDictionary<string, string> GetOrLoadValues(string languageCode, string localePath)
    {
        var cacheKey = LanguageCodes.Normalize(languageCode) + "|" + localePath;
        var lastWriteUtc = File.Exists(localePath) ? File.GetLastWriteTimeUtc(localePath) : DateTime.MinValue;

        lock (CacheGate)
        {
            CacheEntry? cached;
            if (Cache.TryGetValue(cacheKey, out cached) && cached.LastWriteUtc == lastWriteUtc)
            {
                return cached.Values;
            }

            var loaded = LoadValues(languageCode, localePath);
            Cache[cacheKey] = new CacheEntry(lastWriteUtc, loaded);
            return loaded;
        }
    }

    private static IReadOnlyDictionary<string, string> LoadValues(string languageCode, string localePath)
    {
        var normalizedLanguageCode = LanguageCodes.Normalize(languageCode);
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in LoadEmbeddedValues(LanguageCodes.English))
        {
            merged[pair.Key] = pair.Value;
        }

        foreach (var pair in LoadEmbeddedValues(normalizedLanguageCode))
        {
            merged[pair.Key] = pair.Value;
        }

        if (!File.Exists(localePath))
        {
            return merged;
        }

        try
        {
            foreach (var pair in DeserializeJson(File.ReadAllText(localePath)))
            {
                merged[pair.Key] = pair.Value;
            }
        }
        catch
        {
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, string> LoadEmbeddedValues(string languageCode)
    {
        var assembly = typeof(AppLocalizer).Assembly;
        var resourceName = "StreamRecorder.Core.Locales." + LanguageCodes.Normalize(languageCode) + ".json";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return DeserializeJson(reader.ReadToEnd());
    }

    private static IReadOnlyDictionary<string, string> DeserializeJson(string json)
    {
        var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        return loaded is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(loaded, StringComparer.Ordinal);
    }

    private sealed class CacheEntry
    {
        public CacheEntry(DateTime lastWriteUtc, IReadOnlyDictionary<string, string> values)
        {
            LastWriteUtc = lastWriteUtc;
            Values = values;
        }

        public DateTime LastWriteUtc { get; }

        public IReadOnlyDictionary<string, string> Values { get; }
    }

    public sealed class AvailableLanguage
    {
        public AvailableLanguage(string code, string displayName)
        {
            Code = LanguageCodes.Normalize(code);
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Code : displayName;
        }

        public string Code { get; }

        public string DisplayName { get; }
    }
}
