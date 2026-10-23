using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OzgurGuard.LockerUI.Ipc;
using OzgurGuard.LockerUI.Native;

namespace OzgurGuard.LockerUI;

public partial class MainWindow : Window
{
    private readonly int _sessionId;
    private readonly string _token;
    private readonly bool _devMode;
    private readonly CancellationTokenSource _cts = new();

    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _countdownTimer;
    private readonly DispatcherTimer _focusTrapTimer;

    private IpcClient? _client;
    private bool _isUnlocked;
    private int _remainingSeconds = 45;
    private int _totalSeconds = 45;
    private int _attemptsRemaining = 3;
    private int _maxAttempts = 3;

    private static readonly SolidColorBrush BorderNormal = new(Color.FromRgb(0x33, 0x41, 0x55));
    private static readonly SolidColorBrush BorderError = new(Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly SolidColorBrush TextMuted = new(Color.FromRgb(0x94, 0xA3, 0xB8));
    private static readonly SolidColorBrush TextError = new(Color.FromRgb(0xFC, 0xA5, 0xA5));
    private static readonly SolidColorBrush TextSuccess = new(Color.FromRgb(0x86, 0xEF, 0xAC));

    public MainWindow(int sessionId, string token, bool devMode)
    {
        _sessionId = sessionId;
        _token = token;
        _devMode = devMode;

        InitializeComponent();

        MachineText.Text = $"Sunucu: {Environment.MachineName}";
        UserText.Text = "Kullanıcı: —";
        UpdateClock();

        if (_devMode)
        {
            TestExitButton.Visibility = Visibility.Visible;
        }

        // 1. Saat Zamanlayıcısı
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        // 2. Geri Sayım Zamanlayıcısı
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => OnCountdownTick();

        // 3. 250ms Odak Tuzağı (Focus Trap) & TopMost Zorlama
        _focusTrapTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _focusTrapTimer.Tick += (_, _) => ForceFocusAndTopmost();
        _focusTrapTimer.Start();

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Low-level klavye kancasını kur (Alt+Tab, Win, Ctrl+Esc engelle)
            NativeMethods.InstallKeyboardHook();

            // Görev yöneticisini devre dışı bırak
            NativeMethods.SetTaskManagerDisabled(true);

            // Masaüstü görev çubuğunu gizle
            NativeMethods.SetTaskbarVisible(false);

            this.Activate();
            this.Focus();
            PasswordInput.Focus();

            if (string.IsNullOrWhiteSpace(_token))
            {
                SetStatus("Bağımsız test modunda başlatıldı.", error: false);
                return;
            }

            _client = new IpcClient(_sessionId);

            await _client.ConnectAsync(_token, timeoutMs: 15000, _cts.Token).ConfigureAwait(false);
            await RunReadLoopAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => SetStatus($"Güvenlik servisine bağlanılamadı: {ex.Message}", error: true));
        }
    }

    private async Task RunReadLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested && !_isUnlocked)
            {
                GateMessage? msg = await _client!.ReadAsync(_cts.Token).ConfigureAwait(false);
                if (msg is null) break;

                Dispatcher.Invoke(() => ApplyMessage(msg));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => SetStatus($"Haberleşme kesildi: {ex.Message}", error: true));
        }
    }

    private void ApplyMessage(GateMessage msg)
    {
        try
        {
            switch (msg.Type)
            {
                case "challenge":
                    if (!string.IsNullOrWhiteSpace(msg.UserName))
                    {
                        UserText.Text = $"Kullanıcı: {msg.UserName}";
                    }

                    _maxAttempts = msg.MaxAttempts > 0 ? msg.MaxAttempts : 3;
                    _attemptsRemaining = msg.AttemptsRemaining;
                    _remainingSeconds = msg.RemainingSeconds > 0 ? msg.RemainingSeconds : 45;
                    _totalSeconds = _remainingSeconds;

                    CountdownBar.Maximum = _totalSeconds;
                    CountdownBar.Value = _remainingSeconds;
                    CountdownText.Text = $"Kalan süre: {_remainingSeconds} sn";
                    AttemptsText.Text = $"Kalan deneme: {_attemptsRemaining}/{_maxAttempts}";

                    if (msg.DevelopmentMode)
                    {
                        TestExitButton.Visibility = Visibility.Visible;
                    }

                    _countdownTimer.Start();
                    SetStatus("Lütfen yönetici doğrulama parolasını girin.", error: false);
                    PasswordInput.Focus();
                    break;

                case "result":
                    _attemptsRemaining = msg.AttemptsRemaining;
                    AttemptsText.Text = $"Kalan deneme: {_attemptsRemaining}/{_maxAttempts}";

                    if (msg.Success)
                    {
                        _isUnlocked = true;
                        SetStatus(msg.Message ?? "Doğrulama başarılı. Erişim verildi.", error: false, success: true);
                        CleanupAndClose();
                        return;
                    }

                    SetStatus(msg.Message ?? "Hatalı parola.", error: true);
                    FlashErrorShake();
                    PasswordInput.Clear();
                    PasswordInput.Focus();
                    break;
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Arayüz güncelleme hatası: {ex.Message}", error: true);
        }
    }

    private async void VerifyButton_Click(object sender, RoutedEventArgs e)
    {
        await SubmitPasswordAsync();
    }

    private async void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await SubmitPasswordAsync();
        }
    }

    private async Task SubmitPasswordAsync()
    {
        if (_isUnlocked || _client is null || !_client.IsConnected) return;

        string pwd = PasswordInput.Password;
        if (string.IsNullOrEmpty(pwd))
        {
            SetStatus("Parola boş olamaz.", error: true);
            FlashErrorShake();
            return;
        }

        VerifyButton.IsEnabled = false;
        try
        {
            await _client.SendAsync(new GateMessage
            {
                Type = "verify",
                Password = pwd
            }, _cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => SetStatus($"Parola gönderilemedi: {ex.Message}", error: true));
        }
        finally
        {
            Dispatcher.Invoke(() => VerifyButton.IsEnabled = true);
        }
    }

    private async void TestExitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client is not null && _client.IsConnected)
        {
            try
            {
                await _client.SendAsync(new GateMessage { Type = "test_exit" }, _cts.Token).ConfigureAwait(false);
            }
            catch { }
        }

        _isUnlocked = true;
        CleanupAndClose();
    }

    private void OnCountdownTick()
    {
        if (_remainingSeconds > 0)
        {
            _remainingSeconds--;
        }

        CountdownText.Text = $"Kalan süre: {_remainingSeconds} sn";
        CountdownBar.Value = _remainingSeconds;
    }

    private void ForceFocusAndTopmost()
    {
        if (_isUnlocked) return;

        try
        {
            IntPtr hWnd = new WindowInteropHelper(this).Handle;
            if (hWnd != IntPtr.Zero)
            {
                IntPtr currentForeground = NativeMethods.GetForegroundWindow();
                if (currentForeground != hWnd)
                {
                    NativeMethods.SetWindowPos(
                        hWnd,
                        NativeMethods.HWND_TOPMOST,
                        0, 0, 0, 0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);

                    NativeMethods.SetForegroundWindow(hWnd);
                }
            }
        }
        catch { }
    }

    private void ClipCursorToWindow()
    {
        // RDP oturumlarında DPI ölçekleme uyumsuzlukları ve fare kilitlenmelerini önlemek
        // amacıyla imleç sınırlanmaz. Tam ekran TopMost pencere ve klavye kancası güvenliği sağlar.
    }

    private void FlashErrorShake()
    {
        CardBorder.BorderBrush = BorderError;

        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-10, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(40))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(10, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(240))));

        CardTranslate.BeginAnimation(TranslateTransform.XProperty, animation);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            CardBorder.BorderBrush = BorderNormal;
        };
        timer.Start();
    }

    private void SetStatus(string text, bool error, bool success = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = success ? TextSuccess : error ? TextError : TextMuted;
    }

    private void UpdateClock()
    {
        ClockText.Text = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
    }

    private void CleanupAndClose()
    {
        // 1. Zamanlayıcıları durdur
        _clockTimer.Stop();
        _countdownTimer.Stop();
        _focusTrapTimer.Stop();

        // 2. Klavye kancasını kaldır
        NativeMethods.UninstallKeyboardHook();

        // 3. Görev yöneticisini tekrar aktif et
        NativeMethods.SetTaskManagerDisabled(false);

        // 4. Görev çubuğunu tekrar göster
        NativeMethods.SetTaskbarVisible(true);

        // 5. Fare imleci kısıtlamasını kaldır
        NativeMethods.ClipCursor(IntPtr.Zero);

        _cts.Cancel();
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isUnlocked)
        {
            // Kullanıcı yetkisiz kapatmaya çalışırsa iptal et
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
