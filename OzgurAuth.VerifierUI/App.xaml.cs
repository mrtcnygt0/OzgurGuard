using System.Windows;

namespace OzgurAuth.VerifierUI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        int sessionId = -1;
        string token = string.Empty;

        for (int i = 0; i < e.Args.Length; i++)
        {
            if (string.Equals(e.Args[i], "--session", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
            {
                int.TryParse(e.Args[++i], out sessionId);
            }
            else if (string.Equals(e.Args[i], "--token", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
            {
                token = e.Args[++i];
            }
        }

        var window = new MainWindow(sessionId, token);
        MainWindow = window;
        window.Show();
    }
}
