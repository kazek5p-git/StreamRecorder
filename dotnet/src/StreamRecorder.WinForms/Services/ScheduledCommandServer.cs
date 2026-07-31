using System.IO.Pipes;
using System.Text;

namespace StreamRecorder.WinForms.Services;

internal sealed class ScheduledCommandServer : IDisposable
{
    private const string PipeName = "StreamRecorder.WinForms.ScheduledCommand";
    private readonly Action<ScheduledCommand> handler;
    private CancellationTokenSource? cancellation;
    private Task? serverTask;

    public ScheduledCommandServer(Action<ScheduledCommand> handler)
    {
        this.handler = handler;
    }

    public void Start()
    {
        if (serverTask is not null)
        {
            return;
        }

        cancellation = new CancellationTokenSource();
        serverTask = Task.Run(() => AcceptLoopAsync(cancellation.Token));
    }

    public static bool TrySend(ScheduledCommand command, int timeoutMilliseconds = 2000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeoutMilliseconds);
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            writer.WriteLine(command.ToWireFormat());
            writer.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var line = await reader.ReadLineAsync();
                if (ScheduledCommand.TryParse(line, out var command) && command is not null)
                {
                    handler(command);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
            }
        }
    }
}
