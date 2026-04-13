namespace StreamRecorder.Core.Models;

public sealed class ScheduleEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StationId { get; set; }

    public bool Enabled { get; set; } = true;

    public DayOfWeek DayOfWeek { get; set; }

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

    public string ToDisplayString(string stationName)
    {
        var action = Action == ScheduleAction.StartRecording ? "Start recording" : "Stop recording";
        return $"{DayOfWeek} {Hour:00}:{Minute:00}:{Second:00} - {action} - {stationName}";
    }
}
