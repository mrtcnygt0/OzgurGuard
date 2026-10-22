using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using OzgurAuth.Service.Ipc;
using OzgurAuth.Service.Native;
using OzgurAuth.Service.Security;

namespace OzgurAuth.Service;

public enum VerificationResult
{
    Pending,
    Success,
    TimedOut,
    MaxAttempts,
    WindowClosed,
    LaunchFailed
}

/// <summary>
/// Tek bir oturumun doğrulama yaşam döngüsü: arayüzü başlatır, NamedPipe protokolünü
/// yürütür, süreyi ve denemeleri izler, sonucu fail-closed kuralına göre uygular.
/// </summary>
public sealed class SessionVerification : IAsyncDisposable
{
    private readonly int _sessionId;
    private readonly GateOptions _options;
    private readonly SessionInterop _interop;
    private readonly ILogger _logger;
    private readonly bool _consoleMode;

    private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource<VerificationResult> _outcome =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly DateTimeOffset _deadline;
    private int _attemptsUsed;
    private volatile bool _succeeded;
    private Process? _uiProcess;
    private GatePipeServer? _pipe;

    public int SessionId => _sessionId;

    public SessionVerification(
        int sessionId,
        GateOptions options,
        SessionInterop interop,
        ILogger logger,
        bool consoleMode)
    {
        _sessionId = sessionId;
        _options = options;
        _interop = interop;
        _logger = logger;
        _consoleMode = consoleMode;
        _deadline = DateTimeOffset.UtcNow.AddSeconds(options.TimeoutSeconds);
    }

    public async Task RunAsync(CancellationToken serviceToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(serviceToken, _cts.Token);
        CancellationToken ct = linked.Token;

        string userName = SafeGetUserName();
        string clientAddress = SafeGetClientAddress();

        _logger.LogInformation(
            "Doğrulama başlatıldı. Oturum={SessionId} Kullanıcı={User} İstemci={Client} TestModu={TestMode}",
            _sessionId, string.IsNullOrEmpty(userName) ? "(bilinmiyor)" : userName,
            string.IsNullOrEmpty(clientAddress) ? "(yerel/konsol)" : clientAddress, _options.TestMode);

        if (!TryLaunchUi(userName))
        {
            await ApplyOutcomeAsync(VerificationResult.LaunchFailed, userName, clientAddress).ConfigureAwait(false);
            return;
        }

        MonitorProcessExit();
        _ = Task.Run(() => RunTimeoutAsync(ct), ct);
        _ = Task.Run(() => RunProtocolAsync(userName, ct), ct);

        VerificationResult result;
        try
        {
            result = await _outcome.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result = VerificationResult.WindowClosed;
        }

        await ApplyOutcomeAsync(result, userName, clientAddress).ConfigureAwait(false);
    }

    private bool TryLaunchUi(string userName)
    {
        string verifierPath = ResolveVerifierPath();
        if (!File.Exists(verifierPath))
        {
            _logger.LogError("Doğrulama arayüzü bulunamadı: {Path}", verifierPath);
            return false;
        }

        string arguments = $"--session {_sessionId} --token {_token}";

        if (_consoleMode)
        {
            try
            {
                _uiProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = verifierPath,
                    Arguments = arguments,
                    UseShellExecute = false
                });
                return _uiProcess is not null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Konsol modunda arayüz başlatılamadı.");
                return false;
            }
        }

        // Servis modu: kullanıcı belirteci birkaç saniye hazır olmayabilir, kısa aralıklarla dene.
        for (int attempt = 0; attempt < 10; attempt++)
        {
            int? pid = _interop.LaunchInSession(_sessionId, verifierPath, arguments);
            if (pid is int processId)
            {
                try
                {
                    _uiProcess = Process.GetProcessById(processId);
                    return true;
                }
                catch (ArgumentException)
                {
                    // süreç zaten çıktıysa yeniden dene
                }
            }

            Thread.Sleep(500);
        }

        _logger.LogError("Arayüz oturum {SessionId} içinde başlatılamadı.", _sessionId);
        return false;
    }

    private void MonitorProcessExit()
    {
        if (_uiProcess is null)
        {
            return;
        }

        try
        {
            _uiProcess.EnableRaisingEvents = true;
            _uiProcess.Exited += (_, _) =>
            {
                if (!_succeeded)
                {
                    _outcome.TrySetResult(VerificationResult.WindowClosed);
                }
            };

            if (_uiProcess.HasExited && !_succeeded)
            {
                _outcome.TrySetResult(VerificationResult.WindowClosed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Süreç çıkış izleme kurulamadı.");
        }
    }

    private async Task RunTimeoutAsync(CancellationToken ct)
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

        if (!_succeeded)
        {
            _outcome.TrySetResult(VerificationResult.TimedOut);
        }
    }

    private async Task RunProtocolAsync(string userName, CancellationToken ct)
    {
        try
        {
            _pipe = GatePipeServer.Create(_sessionId, _consoleMode ? null : _interop.GetUserSid(_sessionId));

            await _pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);

            GateMessage? hello = await _pipe.ReadMessageAsync(ct).ConfigureAwait(false);
            if (hello is null || hello.Type != "hello" || hello.Token != _token)
            {
                _logger.LogWarning("Oturum {SessionId}: geçersiz hello/token; bağlantı reddedildi.", _sessionId);
                return; // token eşleşmezse protokolü bitir; zaman aşımı devreye girer
            }

            await _pipe.SendMessageAsync(BuildChallenge(userName), ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested && !_succeeded)
            {
                GateMessage? msg = await _pipe.ReadMessageAsync(ct).ConfigureAwait(false);
                if (msg is null)
                {
                    return; // arayüz boruyu kapattı; Process.Exited sonucu belirleyecek
                }

                if (msg.Type != "verify")
                {
                    continue;
                }

                bool ok = PasswordVerifier.Verify(msg.Password ?? string.Empty, _options.PasswordSalt, _options.PasswordHash);
                if (ok)
                {
                    _succeeded = true; // başarı bayrağı, cevaptan ÖNCE yazılır
                    await _pipe.SendMessageAsync(new GateMessage
                    {
                        Type = "result",
                        Success = true,
                        Action = GateActions.Close,
                        Message = "Doğrulama başarılı. Oturuma erişim verildi.",
                        AttemptsRemaining = Math.Max(0, _options.MaxAttempts - _attemptsUsed),
                        RemainingSeconds = RemainingSeconds()
                    }, ct).ConfigureAwait(false);

                    _outcome.TrySetResult(VerificationResult.Success);
                    return;
                }

                _attemptsUsed++;
                int remaining = _options.MaxAttempts - _attemptsUsed;
                _logger.LogWarning("Oturum {SessionId}: hatalı parola. Kullanılan deneme {Used}/{Max}.",
                    _sessionId, _attemptsUsed, _options.MaxAttempts);

                if (remaining <= 0)
                {
                    await _pipe.SendMessageAsync(new GateMessage
                    {
                        Type = "result",
                        Success = false,
                        Action = GateActions.Disconnected,
                        Message = "Deneme hakkı bitti. Oturum sonlandırılıyor.",
                        AttemptsRemaining = 0,
                        RemainingSeconds = RemainingSeconds()
                    }, ct).ConfigureAwait(false);

                    _outcome.TrySetResult(VerificationResult.MaxAttempts);
                    return;
                }

                await _pipe.SendMessageAsync(new GateMessage
                {
                    Type = "result",
                    Success = false,
                    Action = GateActions.None,
                    Message = "Hatalı parola.",
                    AttemptsRemaining = remaining,
                    MaxAttempts = _options.MaxAttempts,
                    RemainingSeconds = RemainingSeconds()
                }, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // normal kapanış
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Oturum {SessionId}: pipe protokolünde hata.", _sessionId);
        }
    }

    private GateMessage BuildChallenge(string userName) => new()
    {
        Type = "challenge",
        RemainingSeconds = RemainingSeconds(),
        AttemptsRemaining = _options.MaxAttempts - _attemptsUsed,
        MaxAttempts = _options.MaxAttempts,
        TestMode = _consoleMode || _options.TestMode,
        UserName = userName
    };

    private int RemainingSeconds()
    {
        int seconds = (int)Math.Ceiling((_deadline - DateTimeOffset.UtcNow).TotalSeconds);
        return Math.Max(0, seconds);
    }

    private async Task ApplyOutcomeAsync(VerificationResult result, string userName, string clientAddress)
    {
        if (result == VerificationResult.Success)
        {
            _logger.LogInformation("Oturum {SessionId} doğrulandı. Kullanıcı={User}", _sessionId, userName);
            return;
        }

        string reason = result switch
        {
            VerificationResult.TimedOut => "süre doldu",
            VerificationResult.MaxAttempts => "ardışık hatalı parola sınırı aşıldı",
            VerificationResult.WindowClosed => "doğrulama penceresi kapatıldı",
            VerificationResult.LaunchFailed => "doğrulama arayüzü başlatılamadı",
            _ => "bilinmeyen"
        };

        if (_consoleMode || _options.TestMode)
        {
            _logger.LogWarning(
                "Oturum sonlandırma tetiklendi (Test Modu). Oturum={SessionId} Kullanıcı={User} İstemci={Client} Neden={Reason}",
                _sessionId, userName, string.IsNullOrEmpty(clientAddress) ? "(yerel/konsol)" : clientAddress, reason);
            return;
        }

        _logger.LogWarning(
            "Fail-closed: oturum sonlandırılıyor. Oturum={SessionId} Kullanıcı={User} İstemci={Client} Neden={Reason}",
            _sessionId, userName, string.IsNullOrEmpty(clientAddress) ? "(yerel/konsol)" : clientAddress, reason);

        bool disconnected = _interop.DisconnectSession(_sessionId);
        if (disconnected)
        {
            _logger.LogWarning("Oturum {SessionId} WTSDisconnectSession ile sonlandırıldı.", _sessionId);
        }

        await Task.CompletedTask;
    }

    private string ResolveVerifierPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.VerifierPath))
        {
            return _options.VerifierPath;
        }

        string? serviceDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (serviceDir is null)
        {
            return "OzgurAuth.VerifierUI.exe";
        }

        // Kurulum düzeni: ...\Service ve ...\Verifier kardeş klasörlerdir.
        string sibling = Path.GetFullPath(Path.Combine(serviceDir, "..", "Verifier", "OzgurAuth.VerifierUI.exe"));
        if (File.Exists(sibling))
        {
            return sibling;
        }

        // Geliştirme düzeni: servis çıktısının yanında ya da çözümdeki VerifierUI projesinin bin klasöründe.
        string local = Path.Combine(serviceDir, "OzgurAuth.VerifierUI.exe");
        if (File.Exists(local))
        {
            return local;
        }

        string configuration = serviceDir.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        string repoRoot = Path.GetFullPath(Path.Combine(serviceDir, "..", "..", "..", "..", ".."));
        string devBuild = Path.Combine(
            repoRoot,
            "OzgurAuth.VerifierUI",
            "bin",
            configuration,
            "net8.0-windows",
            "win-x64",
            "OzgurAuth.VerifierUI.exe");
        return File.Exists(devBuild) ? devBuild : sibling;
    }

    private string SafeGetUserName()
    {
        try { return _interop.GetUserName(_sessionId); }
        catch { return string.Empty; }
    }

    private string SafeGetClientAddress()
    {
        try { return _interop.GetClientAddress(_sessionId); }
        catch { return string.Empty; }
    }

    public void Cancel() => _cts.Cancel();

    public async ValueTask DisposeAsync()
    {
        try { _cts.Cancel(); } catch { }

        if (_pipe is not null)
        {
            await _pipe.DisposeAsync().ConfigureAwait(false);
        }

        _uiProcess?.Dispose();
        _cts.Dispose();
    }
}
