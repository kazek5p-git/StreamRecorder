using System.Collections.ObjectModel;

namespace StreamRecorder.Core.Logging;

public sealed class LogBus
{
    private readonly object gate = new();
    private readonly string filePath;
    private readonly List<LogEntry> entries;

    public LogBus(string filePath)
    {
        this.filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        entries = LoadExistingEntries(filePath);
    }

    public event Action<LogEntry>? EntryAdded;

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (gate)
            {
                return new ReadOnlyCollection<LogEntry>(entries.ToList());
            }
        }
    }

    public string EntriesText
    {
        get
        {
            lock (gate)
            {
                return string.Join(Environment.NewLine, entries.Select(static entry => entry.FormatLine()));
            }
        }
    }

    public void Push(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(message));
        }

        var entry = new LogEntry
        {
            Timestamp = DateTimeOffset.Now,
            Message = message,
        };

        lock (gate)
        {
            entries.Add(entry);
            AppendToFile(entry);
        }

        EntryAdded?.Invoke(entry);
    }

    private void AppendToFile(LogEntry entry)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        writer.WriteLine(entry.FormatLine());
    }

    private static List<LogEntry> LoadExistingEntries(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        return File.ReadLines(filePath)
            .Select(line => LogEntry.TryParse(line, out var entry) ? entry : null)
            .Where(static entry => entry is not null)
            .Cast<LogEntry>()
            .ToList();
    }
}
