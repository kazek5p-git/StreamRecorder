using Microsoft.Win32;

namespace StreamRecorder.WinForms.Services;

internal sealed class WindowsStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "StreamRecorder";

    public void Apply(bool enabled, string executablePath, bool installed = false)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            key.SetValue(ValueName, FormatCommand(executablePath, installed), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    internal static string FormatCommand(string executablePath, bool installed = false)
    {
        return installed
            ? $"\"{executablePath}\" --installed"
            : $"\"{executablePath}\"";
    }
}
