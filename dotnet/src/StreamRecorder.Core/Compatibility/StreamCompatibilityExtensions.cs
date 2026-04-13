using System.Runtime.InteropServices;

namespace StreamRecorder.Core.Compatibility;

internal static class StreamCompatibilityExtensions
{
    public static Task<int> ReadAsync(this Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        return stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
    }

    public static Task<int> ReadAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        ArraySegment<byte> segment;
        if (MemoryMarshal.TryGetArray(buffer, out segment) && segment.Array is not null)
        {
            return stream.ReadAsync(segment.Array, segment.Offset, segment.Count, cancellationToken);
        }

        return ReadAsyncFallback(stream, buffer, cancellationToken);
    }

    public static Task WriteAsync(this Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        return stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
    }

    public static Task WriteAsync(this Stream stream, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        ArraySegment<byte> segment;
        if (MemoryMarshal.TryGetArray(buffer, out segment) && segment.Array is not null)
        {
            return stream.WriteAsync(segment.Array, segment.Offset, segment.Count, cancellationToken);
        }

        return stream.WriteAsync(buffer.ToArray(), 0, buffer.Length, cancellationToken);
    }

    public static Task FlushAsync(this Stream stream, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return stream.FlushAsync();
    }

    public static Task CopyToAsync(this Stream source, Stream destination, CancellationToken cancellationToken)
    {
        return source.CopyToAsync(destination, 81920, cancellationToken);
    }

    public static Task DisposeAsync(this Stream stream)
    {
        stream.Dispose();
        return Task.CompletedTask;
    }

    private static async Task<int> ReadAsyncFallback(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var temporary = new byte[buffer.Length];
        var read = await stream.ReadAsync(temporary, 0, temporary.Length, cancellationToken).ConfigureAwait(false);
        if (read > 0)
        {
            temporary.AsSpan(0, read).CopyTo(buffer.Span);
        }

        return read;
    }
}
