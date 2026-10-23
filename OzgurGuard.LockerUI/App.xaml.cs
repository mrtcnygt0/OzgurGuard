using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using OzgurGuard.LockerUI.Native;

namespace OzgurGuard.LockerUI;

public partial class App : Application
{
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "OzgurGuard",
        "locker_crash.log");

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // Global yakalanmamış istisna yöneticileri (0xe0434352 çökmesini engeller)
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

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

        try
        {
            var mainWindow = new MainWindow(sessionId, token, devMode);
            mainWindow.Show();
            mainWindow.Activate();
            mainWindow.Focus();
        }
        catch (Exception ex)
        {
            LogCrash("MainWindow_Startup", ex);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash("DispatcherUnhandledException", e.Exception);
        e.Handled = true; // Windows hata diyalogunun (0xe0434352) açılmasını ve sürecin sonlanmasını engeller
    }

    private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogCrash("CurrentDomainUnhandledException", ex);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void LogCrash(string source, Exception ex)
    {
        try
        {
            string? dir = Path.GetDirectoryName(CrashLogPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(CrashLogPath, logEntry);
        }
        catch { }
    }
}
