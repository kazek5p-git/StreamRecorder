namespace StreamRecorder.Core.Recording;

internal static class MmshStreamReader
{
    public static async Task<MmshChunk?> ReadChunkAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        if (!await TryReadExactlyAsync(stream, header, cancellationToken))
        {
            return null;
        }

        if (header[0] != 0x24)
        {
            throw new InvalidOperationException("Invalid MMSH frame header.");
        }

        var kind = (char)header[1];
        var payloadLength = BitConverter.ToUInt16(header, 2);
        var payload = payloadLength == 0 ? [] : new byte[payloadLength];
        if (payloadLength > 0 && !await TryReadExactlyAsync(stream, payload, cancellationToken))
        {
            return null;
        }

        var data = kind switch
        {
            'H' => payload.Length > 8 ? payload.AsMemory(8).ToArray() : [],
            'D' => payload,
            _ => [],
        };

        return new MmshChunk(kind, data);
    }

    private static async Task<bool> TryReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }
}

internal sealed record MmshChunk(char Kind, byte[] Data);
