using System.Diagnostics;
using System.Security.Cryptography;
using OzgurGuard.Service.Ipc;
using OzgurGuard.Service.Native;
using OzgurGuard.Service.Security;

namespace OzgurGuard.Service.Services;

public sealed class SessionWatchdog : IAsyncDisposable
{
    private readonly int _sessionId;
    private readonly GuardOptions _options;
    private readonly SecurityLogger _secLogger;
    private readonly string _userName;
    private readonly string _clientAddress;
    private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    private readonly CancellationTokenSource _cts = new();

    private readonly DateTimeOffset _deadline;
    private int _attemptsUsed;
    private volatile bool _succeeded;
    private volatile bool _isDisposed;
    private Process? _activeLockerProcess;
    private IpcServer? _pipeServer;

    public int SessionId => _sessionId;
    public bool IsSucceeded => _succeeded;

    public SessionWatchdog(int sessionId, GuardOptions options, SecurityLogger secLogger)
    {
        _sessionId = sessionId;
        _options = options;
        _secLogger = secLogger;
        _userName = NativeMethods.GetSessionUserName(sessionId);
        _clientAddress = NativeMethods.GetClientAddress(sessionId);
        _deadline = DateTimeOffset.UtcNow.AddSeconds(options.TimeoutSeconds);
    }

    public async Task StartAsync(CancellationToken serviceToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(serviceToken, _cts.Token);
        CancellationToken ct = linked.Token;

        _secLogger.LogEvent(
            "INFO",
            _sessionId,
            _userName,
            _clientAddress,
            "SESSION_ATTACH",
            $"OzgurGuard kilit mekanizması devreye girdi. Zaman aşımı={_options.TimeoutSeconds}sn, Deneme={_options.MaxAttempts}, DevMode={_options.DevelopmentMode}");

        // 1. İlk kilit ekranını başlat
        EnsureLockerProcessRunning();

        // 2. 300ms Heartbeat Watchdog döngüsünü başlat (Bekçi mekanizması)
        _ = Task.Run(() => RunHeartbeatWatchdogAsync(ct), ct);

        // 3. 45 saniye zaman aşımı izleyicisini başlat
        _ = Task.Run(() => RunTimeoutWatchAsync(ct), ct);

        // 4. NamedPipe IPC sunucusunu çalıştır
        await RunProtocolLoopAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Kilit ekranı sürecinin çalıştığını doğrular; öldüyse veya kapandıysa
    /// kullanıcı henüz doğrulamayı tamamlamadığı sürece anında diriltir.
    /// </summary>
    private void EnsureLockerProcessRunning()
    {
        if (_succeeded || _isDisposed) return;

        try
        {
            if (_activeLockerProcess is not null && !_activeLockerProcess.HasExited)
            {
                return; // Zaten çalışıyor
            }

            string lockerExe = ResolveLockerPath();
            if (!File.Exists(lockerExe))
            {
                _secLogger.LogEvent("ERROR", _sessionId, _userName, _clientAddress, "LOCKER_NOT_FOUND", $"Kilit arayüzü dosyası bulunamadı: {lockerExe}");
                return;
            }

            string arguments = $"--session {_sessionId} --token {_token} --devMode {_options.DevelopmentMode}";

            // Kullanıcı oturumu token'ı hazır olana kadar kısa aralıklarla dene
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                int? pid = NativeMethods.LaunchInSession(_sessionId, lockerExe, arguments, out int win32Err);

                if (pid is int processId)
                {
                    _activeLockerProcess = Process.GetProcessById(processId);
                    _secLogger.LogEvent("INFO", _sessionId, _userName, _clientAddress, "LOCKER_LAUNCHED", $"Kilit ekranı başlatıldı. PID: {processId}");
                    return;
                }

                if (attempt < 5)
                {
                    Thread.Sleep(300);
                }
                else
                {
                    _secLogger.LogEvent("ERROR", _sessionId, _userName, _clientAddress, "LOCKER_LAUNCH_FAILED", $"Kilit ekranı oturum içine enjekte edilemedi. Win32 Hata Kodu: {win32Err}");
                }
            }
        }
        catch (Exception ex)
        {
            _secLogger.LogEvent("ERROR", _sessionId, _userName, _clientAddress, "LOCKER_EXCEPTION", $"Arayüz başlatılırken hata: {ex.Message}");
        }
    }

    /// <summary>
    /// 300ms Heartbeat Watchdog: Kilit ekranı zorla kapatılsa bile anında yeniden açılır.
    /// </summary>
    private async Task RunHeartbeatWatchdogAsync(CancellationToken ct)
    {
        int interval = Math.Max(100, _options.HeartbeatIntervalMs);

        while (!ct.IsCancellationRequested && !_succeeded)
        {
            try
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);

                if (_succeeded || _isDisposed) break;

                if (_activeLockerProcess is null || _activeLockerProcess.HasExited)
                {
                    _secLogger.LogEvent(
                        "WARN",
                        _sessionId,
                        _userName,
                        _clientAddress,
                        "WATCHDOG_INTERVENE",
                        "Kilit ekranı kapatıldı veya öldürüldü! Watchdog hemen yeniden başlatıyor...");

                    EnsureLockerProcessRunning();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _secLogger.LogEvent("ERROR", _sessionId, _userName, _clientAddress, "WATCHDOG_ERROR", ex.Message);
            }
        }
    }

    /// <summary>
    /// 45 saniyelik zaman aşımı denetimi: süre biterse RDP oturumunu anında koparır.
    /// </summary>
    private async Task RunTimeoutWatchAsync(CancellationToken ct)
    {
        TimeSpan remaining = _deadline - DateTimeOffset.UtcNow;
        if (remaining > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(remaining, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (!_succeeded && !_isDisposed)
        {
            _secLogger.LogEvent(
                "WARN",
                _sessionId,
                _userName,
                _clientAddress,
                "TIMEOUT_DISCONNECT",
                $"{_options.TimeoutSeconds} saniyelik doğrulama süresi doldu. RDP oturumu zorla sonlandırılıyor.");

            ExecuteDisconnect();
        }
    }

    /// <summary>
    /// NamedPipe üzerinden LockerUI ile haberleşir.
    /// </summary>
    private async Task RunProtocolLoopAsync(CancellationToken ct)
    {
        try
        {
            _pipeServer = IpcServer.Create(_sessionId);
            await _pipeServer.WaitForConnectionAsync(ct).ConfigureAwait(false);

            // Hello bekle
            GateMessage? hello = await _pipeServer.ReadMessageAsync(ct).ConfigureAwait(false);
            if (hello is null || hello.Type != "hello" || hello.Token != _token)
            {
                _secLogger.LogEvent("WARN", _sessionId, _userName, _clientAddress, "IPC_TOKEN_MISMATCH", "Kilit ekranından geçersiz token/hello geldi. Bağlantı reddedildi.");
                return;
            }

            // Challenge gönder
            await _pipeServer.SendMessageAsync(new GateMessage
            {
                Type = "challenge",
                RemainingSeconds = Math.Max(0, (int)(_deadline - DateTimeOffset.UtcNow).TotalSeconds),
                AttemptsRemaining = Math.Max(0, _options.MaxAttempts - _attemptsUsed),
                MaxAttempts = _options.MaxAttempts,
                DevelopmentMode = _options.DevelopmentMode,
                UserName = _userName
            }, ct).ConfigureAwait(false);

            // Parola doğrulama döngüsü
            while (!ct.IsCancellationRequested && !_succeeded)
            {
                GateMessage? msg = await _pipeServer.ReadMessageAsync(ct).ConfigureAwait(false);
                if (msg is null) break;

                if (msg.Type == "test_exit" && _options.DevelopmentMode)
                {
                    _succeeded = true;
                    _secLogger.LogEvent("INFO", _sessionId, _userName, _clientAddress, "DEV_MODE_EXIT", "Geliştirici modu: Test Çıkışı ile kilit kaldırıldı.");
                    return;
                }

                if (msg.Type != "verify") continue;

                bool isValid = PasswordManager.Verify(msg.Password ?? string.Empty, _options.PasswordSalt, _options.PasswordHash);

                if (isValid)
                {
                    _succeeded = true;
                    await _pipeServer.SendMessageAsync(new GateMessage
                    {
                        Type = "result",
                        Success = true,
                        Action = GateActions.Close,
                        Message = "Doğrulama başarılı. Oturuma erişim verildi.",
                        AttemptsRemaining = _options.MaxAttempts - _attemptsUsed
                    }, ct).ConfigureAwait(false);

                    _secLogger.LogEvent("INFO", _sessionId, _userName, _clientAddress, "AUTH_SUCCESS", "Kullanıcı doğru parolayı girdi. Masaüstü erişimine izin verildi.");
                    return;
                }

                // Hatalı parola
                _attemptsUsed++;
                int remainingAttempts = _options.MaxAttempts - _attemptsUsed;

                _secLogger.LogEvent(
                    "WARN",
                    _sessionId,
                    _userName,
                    _clientAddress,
                    "AUTH_FAIL",
                    $"Hatalı parola denemesi yapıldı. Kullanılan: {_attemptsUsed}/{_options.MaxAttempts}.");

                if (remainingAttempts <= 0)
                {
                    await _pipeServer.SendMessageAsync(new GateMessage
                    {
                        Type = "result",
                        Success = false,
                        Action = GateActions.Disconnect,
                        Message = "3 kez hatalı parola girildi. RDP bağlantısı kesiliyor.",
                        AttemptsRemaining = 0
                    }, ct).ConfigureAwait(false);

                    _secLogger.LogEvent(
                        "ERROR",
                        _sessionId,
                        _userName,
                        _clientAddress,
                        "BRUTE_FORCE_LOCKOUT",
                        "Maksimum hatalı parola sınırına ulaşıldı! WTSDisconnectSession ile kullanıcı sunucudan atılıyor.");

                    ExecuteDisconnect();
                    return;
                }

                await _pipeServer.SendMessageAsync(new GateMessage
                {
                    Type = "result",
                    Success = false,
                    Action = GateActions.None,
                    Message = "Hatalı parola. Lütfen tekrar deneyin.",
                    AttemptsRemaining = remainingAttempts,
                    MaxAttempts = _options.MaxAttempts,
                    RemainingSeconds = Math.Max(0, (int)(_deadline - DateTimeOffset.UtcNow).TotalSeconds)
                }, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _secLogger.LogEvent("ERROR", _sessionId, _userName, _clientAddress, "IPC_ERROR", ex.Message);
        }
    }

    private void ExecuteDisconnect()
    {
        if (_options.DevelopmentMode)
        {
            _secLogger.LogEvent("WARN", _sessionId, _userName, _clientAddress, "DEV_MODE_DISCONNECT_SKIP", "[Geliştirici Modu Aktif] WTSDisconnectSession çağrısı simüle edildi, oturum kapatılmadı.");
            return;
        }

        NativeMethods.DisconnectSession(_sessionId);
    }

    private string ResolveLockerPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.LockerPath) && File.Exists(_options.LockerPath))
        {
            return _options.LockerPath;
        }

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string candidate = Path.Combine(baseDir, "OzgurGuard.LockerUI.exe");
        if (File.Exists(candidate)) return candidate;

        string sibling = Path.GetFullPath(Path.Combine(baseDir, "..", "Locker", "OzgurGuard.LockerUI.exe"));
        if (File.Exists(sibling)) return sibling;

        return _options.LockerPath;
    }

    public async ValueTask DisposeAsync()
    {
        _isDisposed = true;
        _cts.Cancel();

        if (_pipeServer is not null)
        {
            await _pipeServer.DisposeAsync().ConfigureAwait(false);
        }

        _activeLockerProcess?.Dispose();
        _cts.Dispose();
    }
}
