namespace StreamRecorder.Core.Models;

public static class LanguageCodes
{
    public const string Polish = "pl";
    public const string English = "en";
    public const string Default = Polish;

    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Default;
        }

        var normalized = code.Trim().Replace('_', '-').ToLowerInvariant();
        return normalized.Length == 0 ? Default : normalized;
    }
}
