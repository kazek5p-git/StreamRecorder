using System.Text;

namespace StreamRecorder.Core.Recording;

internal sealed class IcyMetadataReader
{
    private readonly Stream stream;
    private readonly int metadataInterval;
    private readonly Action<string>? titleReceived;
    private int audioBytesRemaining;

    public IcyMetadataReader(Stream stream, int? metadataInterval, Action<string>? titleReceived)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        this.metadataInterval = metadataInterval.GetValueOrDefault();
        this.titleReceived = titleReceived;
        audioBytesRemaining = this.metadataInterval > 0 ? this.metadataInterval : 0;
    }

    public async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (count == 0)
        {
            return 0;
        }

        if (metadataInterval <= 0)
        {
            return await stream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        }

        while (true)
        {
            if (audioBytesRemaining == 0)
            {
                if (!await ReadMetadataBlockAsync(cancellationToken).ConfigureAwait(false))
                {
                    return 0;
                }

                audioBytesRemaining = metadataInterval;
            }

            var requested = Math.Min(count, audioBytesRemaining);
            var read = await stream.ReadAsync(buffer, offset, requested, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return 0;
            }

            audioBytesRemaining -= read;
            return read;
        }
    }

    private async Task<bool> ReadMetadataBlockAsync(CancellationToken cancellationToken)
    {
        var lengthByte = new byte[1];
        if (!await ReadExactlyAsync(lengthByte, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var metadataLength = lengthByte[0] * 16;
        if (metadataLength == 0)
        {
            return true;
        }

        var metadata = new byte[metadataLength];
        if (!await ReadExactlyAsync(metadata, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var title = ParseStreamTitle(metadata);
        if (!string.IsNullOrWhiteSpace(title))
        {
            titleReceived?.Invoke(title!);
        }

        return true;
    }

    private async Task<bool> ReadExactlyAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer, offset, buffer.Length - offset, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    internal static string? ParseStreamTitle(byte[] metadata)
    {
        var text = DecodeMetadata(metadata).Trim('\0', ' ', '\t', '\r', '\n');
        const string marker = "StreamTitle=";
        var markerIndex = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var value = text.Substring(markerIndex + marker.Length).TrimStart();
        if (value.Length == 0)
        {
            return null;
        }

        if (value[0] is '\'' or '"')
        {
            var quote = value[0];
            var closingQuote = FindClosingQuote(value, quote);
            value = closingQuote > 1 ? value.Substring(1, closingQuote - 1) : value.Substring(1);
        }
        else
        {
            var separator = value.IndexOf(';');
            value = separator >= 0 ? value.Substring(0, separator) : value;
        }

        value = value.Replace("\\'", "'", StringComparison.Ordinal)
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int FindClosingQuote(string value, char quote)
    {
        for (var index = 1; index < value.Length; index++)
        {
            if (value[index] == quote && value[index - 1] != '\\')
            {
                return index;
            }
        }

        return -1;
    }

    private static string DecodeMetadata(byte[] metadata)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            return utf8.GetString(metadata);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(metadata);
        }
    }
}
