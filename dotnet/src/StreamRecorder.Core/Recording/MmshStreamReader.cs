namespace StreamRecorder.Core.Recording;

internal static class MmshStreamReader
{
    public static async Task<MmshChunk?> ReadChunkAsync(Stream stream, TimeSpan readTimeout, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        if (!await TryReadExactlyAsync(stream, header, readTimeout, cancellationToken))
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
        if (payloadLength > 0 && !await TryReadExactlyAsync(stream, payload, readTimeout, cancellationToken))
        {
            return null;
        }

        var data = kind switch
        {
            'H' => payload.Length > 8 ? payload.Skip(8).ToArray() : [],
            'D' => payload,
            _ => [],
        };

        return new MmshChunk(kind, data);
    }

    private static async Task<bool> TryReadExactlyAsync(Stream stream, byte[] buffer, TimeSpan readTimeout, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await ReadWithTimeoutAsync(stream, buffer, offset, buffer.Length - offset, readTimeout, cancellationToken);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static async Task<int> ReadWithTimeoutAsync(
        Stream stream,
        byte[] buffer,
        int offset,
        int count,
        TimeSpan readTimeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var readTask = stream.ReadAsync(buffer, offset, count, cancellationToken);
        _ = readTask.ContinueWith(static task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        var delayTask = Task.Delay(readTimeout, cancellationToken);
        var completed = await Task.WhenAny(readTask, delayTask).ConfigureAwait(false);
        if (completed == readTask)
        {
            return await readTask.ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException($"No MMSH data was received for {readTimeout.TotalSeconds:0} seconds.");
    }
}

internal sealed record MmshChunk(char Kind, byte[] Data);
