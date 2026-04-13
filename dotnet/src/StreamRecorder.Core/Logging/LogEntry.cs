namespace StreamRecorder.Core.Logging;

public sealed class LogEntry
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    public string Message { get; set; } = string.Empty;

    public string FormatLine()
    {
        return $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] {Message}";
    }

    public static bool TryParse(string line, out LogEntry? entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("[", StringComparison.Ordinal))
        {
            return false;
        }

        var separatorIndex = line.IndexOf("] ", StringComparison.Ordinal);
        if (separatorIndex <= 1)
        {
            return false;
        }

        var timestampText = line.Substring(1, separatorIndex - 1);
        if (!DateTime.TryParseExact(
                timestampText,
                "yyyy-MM-dd HH:mm:ss",
                null,
                System.Globalization.DateTimeStyles.AssumeLocal,
                out var timestamp))
        {
            return false;
        }

        entry = new LogEntry
        {
            Timestamp = new DateTimeOffset(timestamp),
            Message = line.Substring(separatorIndex + 2),
        };
        return true;
    }
}
