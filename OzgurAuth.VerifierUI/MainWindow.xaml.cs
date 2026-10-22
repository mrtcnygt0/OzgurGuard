using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OzgurAuth.VerifierUI.Ipc;

namespace OzgurAuth.VerifierUI;

public partial class MainWindow : Window
{
    private readonly int _sessionId;
    private readonly string _token;
    private readonly CancellationTokenSource _cts = new();
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _countdownTimer;

    private GatePipeClient? _client;
    private bool _allowClose;
    private bool _testMode;
    private bool _ready;
    private int _remaining;
    private int _total = 45;
    private int _attemptsRemaining;
    private int _maxAttempts = 3;

    private static readonly SolidColorBrush NormalBorder = Freeze(Color.FromRgb(0x33, 0x41, 0x55));
    private static readonly SolidColorBrush ErrorBorder = Freeze(Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly SolidColorBrush Muted = Freeze(Color.FromRgb(0x94, 0xA3, 0xB8));
    private static readonly SolidColorBrush ErrorText = Freeze(Color.FromRgb(0xFC, 0xA5, 0xA5));
    private static readonly SolidColorBrush OkText = Freeze(Color.FromRgb(0x86, 0xEF, 0xAC));

    public MainWindow(int sessionId, string token)
    {
        _sessionId = sessionId;
        _token = token;

        InitializeComponent();

        MachineText.Text = $"Sunucu: {Environment.MachineName}";
        UserText.Text = "Kullanıcı: —";
        UpdateClock();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => OnCountdownTick();

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_sessionId < 0 || string.IsNullOrWhiteSpace(_token))
        {
            ShowStatus("Bu pencere OzgurAuth hizmeti tarafından başlatılmalıdır.", error: true);
            return;
        }

        _client = new GatePipeClient(_sessionId);
        try
        {
            await _client.ConnectAsync(_token, timeoutMs: 15000, _cts.Token).ConfigureAwait(true);
            await ReadLoopAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // pencere kapanıyor
        }
        catch (Exception ex)
        {
            ShowStatus($"Doğrulama hizmetine bağlanılamadı. {ex.Message}", error: true);
        }
    }

    private async Task ReadLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            GateMessage? message = await _client!.ReadAsync(_cts.Token).ConfigureAwait(true);
            if (message is null)
            {
                if (!_allowClose)
                {
                    ShowStatus(_testMode
                        ? "Oturum sonlandırma tetiklendi (Test Modu)."
                        : "Doğrulama hizmeti bağlantıyı kapattı.", error: !_testMode);
                }

                return;
            }

            ApplyMessage(message);
        }
    }

    private void ApplyMessage(GateMessage message)
    {
        switch (message.Type)
        {
            case "challenge":
                _testMode = message.TestMode;
                _attemptsRemaining = message.AttemptsRemaining;
                _maxAttempts = message.MaxAttempts > 0 ? message.MaxAttempts : 3;
                if (!string.IsNullOrWhiteSpace(message.UserName))
                {
                    UserText.Text = $"Kullanıcı: {message.UserName}";
                }

                ApplyCountdown(message.RemainingSeconds, resetTotal: true);
                _countdownTimer.Start();
                _ready = true;
                PasswordInput.IsEnabled = true;
                VerifyButton.IsEnabled = true;
                PasswordInput.Focus();
                ShowStatus(_testMode
                    ? "Test modu açık. Oturum sonlandırma simüle edilir."
                    : "Parolanızı girin.", error: false);
                UpdateAttempts();
                break;

            case "result":
                ApplyCountdown(message.RemainingSeconds, resetTotal: false);
                _attemptsRemaining = message.AttemptsRemaining;
                UpdateAttempts();

                if (message.Success)
                {
                    _allowClose = true;
                    ShowStatus(string.IsNullOrWhiteSpace(message.Message)
                        ? "Doğrulama başarılı."
                        : message.Message, error: false, success: true);
                    Close();
                    return;
                }

                ShowStatus(string.IsNullOrWhiteSpace(message.Message) ? "Hatalı parola." : message.Message, error: true);
                FlashError();
                PasswordInput.Clear();
                PasswordInput.Focus();

                if (message.Action == GateActions.Disconnected)
                {
                    _allowClose = true;
                    _ready = false;
                    PasswordInput.IsEnabled = false;
                    VerifyButton.IsEnabled = false;
                    if (_testMode)
                    {
                        ShowStatus("Oturum sonlandırma tetiklendi (Test Modu).", error: true);
                    }
                }

                break;
        }
    }

    private async void VerifyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || _client is null || !_client.IsConnected)
        {
            return;
        }

        string password = PasswordInput.Password;
        if (string.IsNullOrEmpty(password))
        {
            ShowStatus("Parola boş olamaz.", error: true);
            FlashError();
            return;
        }

        VerifyButton.IsEnabled = false;
        try
        {
            await _client.SendPasswordAsync(password, _cts.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowStatus($"Parola gönderilemedi. {ex.Message}", error: true);
            VerifyButton.IsEnabled = _ready;
            return;
        }

        VerifyButton.IsEnabled = _ready;
    }

    private void OnCountdownTick()
    {
        if (_remaining > 0)
        {
            _remaining--;
        }

        CountdownText.Text = $"Kalan süre: {_remaining} sn";
        CountdownBar.Value = _remaining;
    }

    private void ApplyCountdown(int remainingSeconds, bool resetTotal)
    {
        _remaining = Math.Max(0, remainingSeconds);
        if (resetTotal || _total <= 0)
        {
            _total = Math.Max(_remaining, 1);
            CountdownBar.Maximum = _total;
        }

        CountdownText.Text = $"Kalan süre: {_remaining} sn";
        CountdownBar.Value = Math.Min(_remaining, _total);
    }

    private void UpdateAttempts()
    {
        AttemptsText.Text = $"Kalan deneme: {_attemptsRemaining}/{_maxAttempts}";
    }

    private void UpdateClock()
    {
        ClockText.Text = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
    }

    private void ShowStatus(string text, bool error, bool success = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = success ? OkText : error ? ErrorText : Muted;
    }

    private void FlashError()
    {
        CardBorder.BorderBrush = ErrorBorder;
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(40))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220))));
        CardTranslate.BeginAnimation(TranslateTransform.XProperty, animation);

        var revert = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        revert.Tick += (_, _) =>
        {
            revert.Stop();
            CardBorder.BorderBrush = NormalBorder;
        };
        revert.Start();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            string warning = "Doğrulama yapmadan pencereyi kapatırsanız RDP bağlantınız sonlandırılacaktır.";
            if (_testMode)
            {
                warning += Environment.NewLine + Environment.NewLine + "Oturum sonlandırma tetiklendi (Test Modu). Bağlantı kesilmeyecek.";
            }

            MessageBoxResult choice = MessageBox.Show(
                this,
                warning,
                "Özgür Zaman Kontrol Sistemleri",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (choice != MessageBoxResult.OK)
            {
                e.Cancel = true;
                return;
            }
        }

        _cts.Cancel();
        _clockTimer.Stop();
        _countdownTimer.Stop();
        base.OnClosing(e);
    }

    protected override async void OnClosed(EventArgs e)
    {
        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(true);
        }

        _cts.Dispose();
        base.OnClosed(e);
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
