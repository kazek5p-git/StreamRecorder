using StreamRecorder.Core;
using StreamRecorder.Core.Configuration;
using StreamRecorder.WinForms.Forms;

namespace StreamRecorder.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var paths = AppPaths.Discover();
        using var app = new StreamRecorderApp("0.1.7-dev", paths);
        Application.Run(new MainForm(app));
    }
}
