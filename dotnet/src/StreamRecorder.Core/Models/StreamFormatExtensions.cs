namespace StreamRecorder.Core.Models;

public static class StreamFormatExtensions
{
    public static string GetExtension(this StreamFormat format)
    {
        return format switch
        {
            StreamFormat.Mp3 => "mp3",
            StreamFormat.AacRaw => "aac",
            StreamFormat.Ogg => "ogg",
            StreamFormat.Flac => "flac",
            StreamFormat.Wma => "wma",
            StreamFormat.Wav => "wav",
            StreamFormat.MpegTs => "ts",
            _ => "bin",
        };
    }

    public static string GetDisplayName(this StreamFormat format)
    {
        return GetDisplayName(format, LanguageCodes.English);
    }

    public static string GetDisplayName(this StreamFormat format, string languageCode)
    {
        var normalizedLanguage = LanguageCodes.Normalize(languageCode);
        return format switch
        {
            StreamFormat.Mp3 => "MP3",
            StreamFormat.AacRaw => "AAC",
            StreamFormat.Ogg => "OGG",
            StreamFormat.Flac => "FLAC",
            StreamFormat.Wma => "WMA",
            StreamFormat.Wav => "WAV",
            StreamFormat.MpegTs => "MPEG-TS",
            _ => normalizedLanguage == LanguageCodes.Polish ? "Nieznany" : "Unknown",
        };
    }
}
