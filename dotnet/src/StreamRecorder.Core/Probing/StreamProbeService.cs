using StreamRecorder.Core.Models;

namespace StreamRecorder.Core.Probing;

public static class StreamProbeService
{
    public static StreamProbe ProbeStream(string url, string? contentType, byte[] firstBytes)
    {
        var normalizedContentType = NormalizeContentType(contentType);
        var protocol = IsHls(url, normalizedContentType, firstBytes) ? StreamProtocol.Hls : StreamProtocol.Http;
        var format = DetectFormat(url, normalizedContentType, firstBytes);

        return new StreamProbe
        {
            Protocol = protocol,
            Format = format,
            Mime = normalizedContentType,
        };
    }

    private static string? NormalizeContentType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Split(';', 2, StringSplitOptions.TrimEntries)[0].Trim().ToLowerInvariant();
    }

    private static bool IsHls(string url, string? contentType, byte[] firstBytes)
    {
        return url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)
            || (contentType is not null && (
                contentType.Contains("application/vnd.apple.mpegurl", StringComparison.Ordinal)
                || contentType.Contains("application/x-mpegurl", StringComparison.Ordinal)
                || contentType.Contains("audio/mpegurl", StringComparison.Ordinal)))
            || firstBytes.AsSpan().StartsWith("#EXTM3U"u8);
    }

    private static StreamFormat DetectFormat(string url, string? contentType, byte[] firstBytes)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            if (contentType.Contains("audio/mpeg", StringComparison.Ordinal) || contentType.Contains("audio/mp3", StringComparison.Ordinal))
            {
                return StreamFormat.Mp3;
            }
            if (contentType.Contains("audio/aac", StringComparison.Ordinal)
                || contentType.Contains("audio/aacp", StringComparison.Ordinal)
                || contentType.Contains("audio/x-aac", StringComparison.Ordinal))
            {
                return StreamFormat.AacRaw;
            }
            if (contentType.Contains("audio/ogg", StringComparison.Ordinal) || contentType.Contains("application/ogg", StringComparison.Ordinal))
            {
                return StreamFormat.Ogg;
            }
            if (contentType.Contains("audio/flac", StringComparison.Ordinal) || contentType.Contains("application/flac", StringComparison.Ordinal))
            {
                return StreamFormat.Flac;
            }
            if (contentType.Contains("audio/x-ms-wma", StringComparison.Ordinal)
                || contentType.Contains("audio/wma", StringComparison.Ordinal)
                || contentType.Contains("application/vnd.ms-asf", StringComparison.Ordinal))
            {
                return StreamFormat.Wma;
            }
            if (contentType.Contains("audio/wav", StringComparison.Ordinal) || contentType.Contains("audio/x-wav", StringComparison.Ordinal))
            {
                return StreamFormat.Wav;
            }
            if (contentType.Contains("video/mp2t", StringComparison.Ordinal))
            {
                return StreamFormat.MpegTs;
            }
        }

        if (firstBytes.AsSpan().StartsWith("OggS"u8))
        {
            return StreamFormat.Ogg;
        }
        if (firstBytes.AsSpan().StartsWith("fLaC"u8))
        {
            return StreamFormat.Flac;
        }
        if (firstBytes.Length >= 12 && firstBytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && firstBytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            return StreamFormat.Wav;
        }
        if (firstBytes.AsSpan().StartsWith("ID3"u8))
        {
            return StreamFormat.Mp3;
        }
        if (LooksLikeAdts(firstBytes))
        {
            return StreamFormat.AacRaw;
        }
        if (LooksLikeMpegTs(firstBytes))
        {
            return StreamFormat.MpegTs;
        }
        if (firstBytes.Length >= 8 && firstBytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11 }))
        {
            return StreamFormat.Wma;
        }

        var urlLower = url.ToLowerInvariant();
        if (urlLower.EndsWith(".mp3", StringComparison.Ordinal))
        {
            return StreamFormat.Mp3;
        }
        if (urlLower.EndsWith(".aac", StringComparison.Ordinal))
        {
            return StreamFormat.AacRaw;
        }
        if (urlLower.EndsWith(".ogg", StringComparison.Ordinal) || urlLower.EndsWith(".opus", StringComparison.Ordinal))
        {
            return StreamFormat.Ogg;
        }
        if (urlLower.EndsWith(".flac", StringComparison.Ordinal))
        {
            return StreamFormat.Flac;
        }
        if (urlLower.EndsWith(".wma", StringComparison.Ordinal) || urlLower.EndsWith(".asf", StringComparison.Ordinal))
        {
            return StreamFormat.Wma;
        }
        if (urlLower.EndsWith(".wav", StringComparison.Ordinal))
        {
            return StreamFormat.Wav;
        }
        if (urlLower.EndsWith(".ts", StringComparison.Ordinal))
        {
            return StreamFormat.MpegTs;
        }

        return StreamFormat.Unknown;
    }

    private static bool LooksLikeAdts(byte[] bytes)
    {
        return bytes.Length >= 2 && bytes[0] == 0xFF && (bytes[1] & 0xF6) == 0xF0;
    }

    private static bool LooksLikeMpegTs(byte[] bytes)
    {
        return bytes.Length >= 376 && bytes[0] == 0x47 && bytes[188] == 0x47;
    }
}
