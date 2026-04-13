using System.Diagnostics;
using System.Threading.Tasks;

namespace System.Diagnostics;

internal static class ProcessCompatibilityExtensions
{
    public static Task WaitForExitAsync(this Process process, CancellationToken cancellationToken)
    {
        if (process is null)
        {
            throw new ArgumentNullException(nameof(process));
        }

        if (process.HasExited)
        {
            return Task.FromResult(0);
        }

        var tcs = new TaskCompletionSource<object>();
        EventHandler handler = null;
        handler = (_, _) =>
        {
            process.Exited -= handler;
            tcs.TrySetResult(null);
        };

        process.EnableRaisingEvents = true;
        process.Exited += handler;

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() =>
            {
                process.Exited -= handler;
                tcs.TrySetCanceled();
            });
        }

        if (process.HasExited)
        {
            process.Exited -= handler;
            tcs.TrySetResult(null);
        }

        return tcs.Task;
    }
}
