namespace StreamRecorder.Core.Models;

public sealed class AppConfig
{
    public AppSettings Settings { get; set; } = new();

    public List<Station> Stations { get; set; } = [];

    public List<ScheduleEntry> Schedules { get; set; } = [];
}
