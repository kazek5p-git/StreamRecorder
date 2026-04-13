using StreamRecorder.Core.Localization;
using StreamRecorder.Core.Models;

namespace StreamRecorder.Core.Tests;

public sealed class AppLocalizerTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"sr_localizer_{Guid.NewGuid():N}");

    [Fact]
    public void LoadsOverridesFromJsonFiles()
    {
        var localesDirectory = Path.Combine(tempRoot, "locales");
        Directory.CreateDirectory(localesDirectory);
        File.WriteAllText(
            Path.Combine(localesDirectory, "pl.json"),
            "{\n  \"AddStation\": \"&Dodaj własną stację\",\n  \"CurrentlyRecording\": \"Nagrywa teraz: {0}\"\n}");

        var localizer = AppLocalizer.For(LanguageCodes.Polish, tempRoot);

        Assert.Equal("&Dodaj własną stację", localizer.AddStation);
        Assert.Equal("Nagrywa teraz: 3", localizer.CurrentlyRecording(3));
        Assert.Equal("Ustawienia", localizer.SettingsTitle);
    }

    [Fact]
    public void DetectsAvailableLanguagesFromLocaleFiles()
    {
        var localesDirectory = Path.Combine(tempRoot, "locales");
        Directory.CreateDirectory(localesDirectory);
        File.WriteAllText(Path.Combine(localesDirectory, "de.json"), "{\n  \"LanguageName\": \"Deutsch\"\n}");
        File.WriteAllText(Path.Combine(localesDirectory, "cs.json"), "{\n  \"LanguageName\": \"Čeština\"\n}");

        var languages = AppLocalizer.GetAvailableLanguages(tempRoot);

        Assert.Contains(languages, language => language.Code == "de" && language.DisplayName == "Deutsch");
        Assert.Contains(languages, language => language.Code == "cs" && language.DisplayName == "Čeština");
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, true);
        }
    }
}
