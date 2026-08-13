namespace StreamRecorder.Core.Playback;

public sealed class AudioOutputDevice
{
    public AudioOutputDevice(string id, string name, string driver, int index, bool isSystemDefault, bool isAvailable)
    {
        Id = id;
        Name = name;
        Driver = driver;
        Index = index;
        IsSystemDefault = isSystemDefault;
        IsAvailable = isAvailable;
    }

    public string Id { get; }

    public string Name { get; }

    public string Driver { get; }

    public int Index { get; }

    public bool IsSystemDefault { get; }

    public bool IsAvailable { get; }

    public static AudioOutputDevice SystemDefault(string name)
    {
        return new AudioOutputDevice(string.Empty, name, string.Empty, -1, isSystemDefault: true, isAvailable: true);
    }
}
