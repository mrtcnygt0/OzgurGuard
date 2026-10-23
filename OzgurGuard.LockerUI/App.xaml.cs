using System.Diagnostics;
using System.Windows;
using OzgurGuard.LockerUI.Native;

namespace OzgurGuard.LockerUI;

public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        int sessionId = Process.GetCurrentProcess().SessionId;
        string token = string.Empty;
        bool devMode = false;

        for (int i = 0; i < e.Args.Length; i++)
        {
            if (string.Equals(e.Args[i], "--session", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
            {
                int.TryParse(e.Args[i + 1], out sessionId);
            }
            else if (string.Equals(e.Args[i], "--token", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
            {
                token = e.Args[i + 1];
            }
            else if (string.Equals(e.Args[i], "--devMode", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
            {
                bool.TryParse(e.Args[i + 1], out devMode);
            }
        }

        try
        {
            // DACL koruması uygula
            NativeMethods.ProtectCurrentProcess();
        }
        catch { }

        var mainWindow = new MainWindow(sessionId, token, devMode);
        mainWindow.Show();
        mainWindow.Activate();
        mainWindow.Focus();
    }
}
