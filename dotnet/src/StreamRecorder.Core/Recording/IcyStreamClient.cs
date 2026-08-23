using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using StreamRecorder.Core.Compatibility;
using StreamRecorder.Core.Models;

namespace StreamRecorder.Core.Recording;

internal static class IcyStreamClient
{
    public static async Task<IcyStreamResponse> OpenAsync(string userAgent, Station station, CancellationToken cancellationToken)
    {
        var uri = new Uri(station.Url, UriKind.Absolute);
        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ICY fallback supports only HTTP and HTTPS stream URLs.");
        }

        var tcpClient = new TcpClient();
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                tcpClient.Close();
            }
            catch
            {
            }
        });

        await ConnectAsync(tcpClient, uri, cancellationToken);

        Stream transport = tcpClient.GetStream();
        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            var sslStream = new SslStream(transport, false);
            await sslStream.AuthenticateAsClientAsync(uri.Host);
            transport = sslStream;
        }

        var requestBytes = BuildRequest(userAgent, station, uri);
        await transport.WriteAsync(requestBytes, cancellationToken);
        await transport.FlushAsync(cancellationToken);

        var response = await ReadResponseAsync(tcpClient, transport, cancellationToken);
        return response;
    }

    private static async Task ConnectAsync(TcpClient tcpClient, Uri uri, CancellationToken cancellationToken)
    {
        var port = uri.IsDefaultPort
            ? (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80)
            : uri.Port;

        var connectTask = tcpClient.ConnectAsync(uri.Host, port);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
        var completed = await Task.WhenAny(connectTask, timeoutTask);
        if (completed != connectTask)
        {
            throw new TimeoutException("Timed out while connecting to the ICY stream server.");
        }

        await connectTask;
    }

    private static byte[] BuildRequest(string userAgent, Station station, Uri uri)
    {
        var hostHeader = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        var pathAndQuery = string.IsNullOrWhiteSpace(uri.PathAndQuery) ? "/" : uri.PathAndQuery;

        var builder = new StringBuilder();
        builder.Append("GET ").Append(pathAndQuery).Append(" HTTP/1.0\r\n");
        builder.Append("Host: ").Append(hostHeader).Append("\r\n");
        builder.Append("User-Agent: ").Append(userAgent).Append("\r\n");
        builder.Append("Accept: */*\r\n");
        builder.Append("Icy-MetaData: 1\r\n");
        builder.Append("Cache-Control: no-cache\r\n");
        builder.Append("Connection: close\r\n");

        if (station.Credentials is not null && !string.IsNullOrWhiteSpace(station.Credentials.Username))
        {
            var raw = $"{station.Credentials.Username}:{station.Credentials.Password}";
            builder.Append("Authorization: Basic ")
                .Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)))
                .Append("\r\n");
        }

        builder.Append("\r\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static async Task<IcyStreamResponse> ReadResponseAsync(TcpClient tcpClient, Stream transport, CancellationToken cancellationToken)
    {
        const int MaxHeaderBytes = 64 * 1024;
        var headerBytes = new List<byte>(1024);
        var singleByte = new byte[1];

        while (headerBytes.Count < MaxHeaderBytes)
        {
            var read = await transport.ReadAsync(singleByte, cancellationToken);
            if (read == 0)
            {
                break;
            }

            headerBytes.Add(singleByte[0]);
            if (HasHeaderTerminator(headerBytes))
            {
                break;
            }
        }

        var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
        var lines = headerText
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        if (lines.Length == 0)
        {
            throw new InvalidOperationException("The ICY server returned an empty response.");
        }

        var statusLine = lines[0].Trim();
        if (!statusLine.StartsWith("ICY 200", StringComparison.OrdinalIgnoreCase)
            && !statusLine.StartsWith("HTTP/1.0 200", StringComparison.OrdinalIgnoreCase)
            && !statusLine.StartsWith("HTTP/1.1 200", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported ICY status line: {statusLine}");
        }

        string? contentType = null;
        int? metadataInterval = null;
        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var name = line.Substring(0, separator).Trim();
            var value = line.Substring(separator + 1).Trim();
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                contentType = value;
            }
            else if (name.Equals("icy-metaint", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value, out var parsedMetadataInterval)
                && parsedMetadataInterval > 0)
            {
                metadataInterval = parsedMetadataInterval;
            }
        }

        return new IcyStreamResponse(tcpClient, transport, contentType, metadataInterval);
    }

    private static bool HasHeaderTerminator(IReadOnlyList<byte> bytes)
    {
        var count = bytes.Count;
        if (count >= 4
            && bytes[count - 4] == '\r'
            && bytes[count - 3] == '\n'
            && bytes[count - 2] == '\r'
            && bytes[count - 1] == '\n')
        {
            return true;
        }

        return count >= 2
            && bytes[count - 2] == '\n'
            && bytes[count - 1] == '\n';
    }
}

internal sealed class IcyStreamResponse : IDisposable
{
    private readonly TcpClient tcpClient;

    public IcyStreamResponse(TcpClient tcpClient, Stream stream, string? contentType, int? metadataInterval)
    {
        this.tcpClient = tcpClient;
        Stream = stream;
        ContentType = contentType;
        MetadataInterval = metadataInterval;
    }

    public Stream Stream { get; }

    public string? ContentType { get; }

    public int? MetadataInterval { get; }

    public void Dispose()
    {
        try
        {
            Stream.Dispose();
        }
        finally
        {
            tcpClient.Close();
        }
    }
}
