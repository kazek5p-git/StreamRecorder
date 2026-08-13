namespace StreamRecorder.Core.Playback;

public enum PlaybackState
{
    Idle = 0,
    Connecting = 1,
    Playing = 2,
    Reconnecting = 3,
    Stopping = 4,
    Stopped = 5,
    Error = 6,
}
