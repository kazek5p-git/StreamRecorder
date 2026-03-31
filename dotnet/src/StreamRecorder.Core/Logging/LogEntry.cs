namespace StreamRecorder.Core.Logging;

public sealed class LogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public string Message { get; init; } = string.Empty;

    public string FormatLine()
    {
        return $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] {Message}";
    }

    public static bool TryParse(string line, out LogEntry? entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(line) || !line.StartsWith('['))
        {
            return false;
        }

        var separatorIndex = line.IndexOf("] ", StringComparison.Ordinal);
        if (separatorIndex <= 1)
        {
            return false;
        }

        var timestampText = line[1..separatorIndex];
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
            Message = line[(separatorIndex + 2)..],
        };
        return true;
    }
}
