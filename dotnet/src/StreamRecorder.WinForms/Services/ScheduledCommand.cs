namespace StreamRecorder.WinForms.Services;

public sealed class ScheduledCommand
{
    public ScheduledCommand(ScheduledCommandKind kind, Guid scheduleId)
    {
        Kind = kind;
        ScheduleId = scheduleId;
    }

    public ScheduledCommandKind Kind { get; }

    public Guid ScheduleId { get; }

    public string ToWireFormat()
    {
        return Kind + "|" + ScheduleId.ToString("D");
    }

    public static bool TryParse(string? value, out ScheduledCommand? command)
    {
        command = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value!.Trim();
        var parts = text.Split('|');
        if (parts.Length != 2 || !Enum.TryParse(parts[0], ignoreCase: true, out ScheduledCommandKind kind))
        {
            return false;
        }

        if (!Guid.TryParse(parts[1], out var scheduleId))
        {
            return false;
        }

        command = new ScheduledCommand(kind, scheduleId);
        return true;
    }
}

public enum ScheduledCommandKind
{
    Start,
    Stop,
}
