using StreamRecorder.Core.Models;

namespace StreamRecorder.Core.Probing;

public sealed class StreamProbe
{
    public StreamProtocol Protocol { get; set; }

    public StreamFormat Format { get; set; }

    public string? Mime { get; set; }

    public string Extension => Format.GetExtension();
}
