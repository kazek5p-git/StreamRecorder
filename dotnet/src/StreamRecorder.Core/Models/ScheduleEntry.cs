namespace StreamRecorder.Core.Models;

public sealed class ScheduleEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StationId { get; set; }

    public bool Enabled { get; set; } = true;

    public List<DayOfWeek> Days { get; set; } = [System.DayOfWeek.Monday];

    public int StartHour { get; set; }

    public int StartMinute { get; set; }

    public int StartSecond { get; set; }

    public int EndHour { get; set; } = 1;

    public int EndMinute { get; set; }

    public int EndSecond { get; set; }

    public DayOfWeek DayOfWeek
    {
        get => GetDays().First();
        set => Days = [value];
    }

    // Zachowane wyłącznie do migracji starszych wpisów zapisanych jako pojedyncza akcja.
    public ScheduleAction Action { get; set; } = ScheduleAction.StartRecording;

    // Zachowane wyłącznie do migracji starszych wpisów zapisanych jako pojedyncza akcja.
    public int Hour { get; set; }

    // Zachowane wyłącznie do migracji starszych wpisów zapisanych jako pojedyncza akcja.
    public int Minute { get; set; }

    // Zachowane wyłącznie do migracji starszych wpisów zapisanych jako pojedyncza akcja.
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

    public TimeSpan GetStartTime()
    {
        return new TimeSpan(StartHour, StartMinute, StartSecond);
    }

    public void SetStartTime(TimeSpan time)
    {
        StartHour = time.Hours;
        StartMinute = time.Minutes;
        StartSecond = time.Seconds;
    }

    public TimeSpan GetEndTime()
    {
        return new TimeSpan(EndHour, EndMinute, EndSecond);
    }

    public void SetEndTime(TimeSpan time)
    {
        EndHour = time.Hours;
        EndMinute = time.Minutes;
        EndSecond = time.Seconds;
    }

    public bool CrossesMidnight()
    {
        return GetEndTime() <= GetStartTime();
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
        return $"{string.Join(", ", GetDays())} {StartHour:00}:{StartMinute:00}:{StartSecond:00}-{EndHour:00}:{EndMinute:00}:{EndSecond:00} - {stationName}";
    }

    private static int DaySortKey(DayOfWeek day)
    {
        return day == System.DayOfWeek.Sunday ? 6 : (int)day - 1;
    }
}
