namespace StreamRecorder.Core.Models;

public sealed class ScheduleEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StationId { get; set; }

    public bool Enabled { get; set; } = true;

    public List<DayOfWeek> Days { get; set; } = [System.DayOfWeek.Monday];

    public DayOfWeek DayOfWeek
    {
        get => GetDays().First();
        set => Days = [value];
    }

    public ScheduleAction Action { get; set; } = ScheduleAction.StartRecording;

    public int Hour { get; set; }

    public int Minute { get; set; }

    public int Second { get; set; }

    public TimeSpan GetTime()
    {
        return new TimeSpan(Hour, Minute, Second);
    }

    public void SetTime(TimeSpan time)
    {
        Hour = time.Hours;
        Minute = time.Minutes;
        Second = time.Seconds;
    }

    public IReadOnlyList<DayOfWeek> GetDays()
    {
        var days = Days is { Count: > 0 }
            ? Days
            : [System.DayOfWeek.Monday];

        return days
            .Distinct()
            .OrderBy(DaySortKey)
            .ToList();
    }

    public string ToDisplayString(string stationName)
    {
        var action = Action == ScheduleAction.StartRecording ? "Start recording" : "Stop recording";
        return $"{string.Join(", ", GetDays())} {Hour:00}:{Minute:00}:{Second:00} - {action} - {stationName}";
    }

    private static int DaySortKey(DayOfWeek day)
    {
        return day == System.DayOfWeek.Sunday ? 6 : (int)day - 1;
    }
}
