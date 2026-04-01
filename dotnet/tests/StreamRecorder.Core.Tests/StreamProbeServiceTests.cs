using StreamRecorder.Core.Models;
using StreamRecorder.Core.Probing;

namespace StreamRecorder.Core.Tests;

public sealed class StreamProbeServiceTests
{
    [Fact]
    public void ProbeStream_PrefersAacForAudioXAacSegmentsWithId3Tag()
    {
        var bytes = new byte[]
        {
            0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x3F, 0x50, 0x52, 0x49, 0x56, 0x00, 0x00,
        };

        var probe = StreamProbeService.ProbeStream(
            "http://example.test/media_0001.aac",
            "audio/x-aac",
            bytes);

        Assert.Equal(StreamFormat.AacRaw, probe.Format);
    }

    [Fact]
    public void ProbeStream_KeepsMp3ForId3TaggedMp3Segments()
    {
        var bytes = new byte[]
        {
            0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x3F, 0x50, 0x52, 0x49, 0x56, 0x00, 0x00,
        };

        var probe = StreamProbeService.ProbeStream(
            "http://example.test/media_0001.mp3",
            "audio/mpeg",
            bytes);

        Assert.Equal(StreamFormat.Mp3, probe.Format);
    }

    [Fact]
    public void ProbeStream_DetectsMmshAndWmaForMmsUrls()
    {
        var bytes = new byte[]
        {
            0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
            0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C,
        };

        var probe = StreamProbeService.ProbeStream(
            "mms://pompuj.mywire.org",
            "application/x-mms-framed",
            bytes);

        Assert.Equal(StreamProtocol.Mmsh, probe.Protocol);
        Assert.Equal(StreamFormat.Wma, probe.Format);
    }
}
