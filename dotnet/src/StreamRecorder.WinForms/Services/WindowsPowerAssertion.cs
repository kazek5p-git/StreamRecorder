using System.Runtime.InteropServices;

namespace StreamRecorder.WinForms.Services;

internal sealed class WindowsPowerAssertion : IDisposable
{
    private bool active;

    public void Apply(bool enabled)
    {
        if (active == enabled)
        {
            return;
        }

        SetThreadExecutionState(enabled
            ? ExecutionState.Continuous | ExecutionState.SystemRequired
            : ExecutionState.Continuous);

        active = enabled;
    }

    public void Dispose()
    {
        Apply(false);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState executionState);

    [Flags]
    private enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        Continuous = 0x80000000,
    }
}
