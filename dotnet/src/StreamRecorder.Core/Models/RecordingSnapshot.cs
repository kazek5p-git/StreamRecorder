namespace StreamRecorder.Core.Models;

public sealed class RecordingSnapshot
{
    public Guid StationId { get; set; }

    public string StationName { get; set; } = string.Empty;

    public bool Active { get; set; }

    public string StateLabel { get; set; } = "Idle";

    public StreamFormat? Format { get; set; }

    public string? OutputPath { get; set; }

    public long BytesWritten { get; set; }

    public int ReconnectCount { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public static RecordingSnapshot CreateInitial(Station station)
    {
        return new RecordingSnapshot
        {
            StationId = station.Id,
            StationName = station.Name,
            Active = false,
            StateLabel = "Idle",
        };
    }
}
