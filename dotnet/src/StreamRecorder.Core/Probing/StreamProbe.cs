using StreamRecorder.Core.Models;

namespace StreamRecorder.Core.Probing;

public sealed class StreamProbe
{
    public required StreamProtocol Protocol { get; init; }

    public required StreamFormat Format { get; init; }

    public string? Mime { get; init; }

    public string Extension => Format.GetExtension();
}
