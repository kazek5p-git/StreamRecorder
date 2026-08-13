namespace StreamRecorder.Core.Playback;

public sealed class PlaybackSnapshot
{
    public Guid StationId { get; set; }

    public bool Active { get; set; }

    public PlaybackState State { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset? StartedAt { get; set; }
}
