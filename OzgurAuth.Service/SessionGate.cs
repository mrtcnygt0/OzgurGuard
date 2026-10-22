using System.Collections.Concurrent;
using System.ServiceProcess;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OzgurAuth.Service.Native;

namespace OzgurAuth.Service;

/// <summary>
/// Oturum değişiklik olaylarını dinler ve her uygun oturum için bir
/// <see cref="SessionVerification"/> başlatır. Session 0 ve yinelenen istekler atlanır.
/// </summary>
public sealed class SessionGate
{
    private readonly GateOptions _options;
    private readonly SessionInterop _interop;
    private readonly ILogger<SessionGate> _logger;
    private readonly ConcurrentDictionary<int, SessionVerification> _active = new();

    private CancellationToken _serviceToken = CancellationToken.None;
    private bool _consoleMode;

    public SessionGate(IOptions<GateOptions> options, SessionInterop interop, ILogger<SessionGate> logger)
    {
        _options = options.Value;
        _interop = interop;
        _logger = logger;
    }

    public void Initialize(CancellationToken serviceToken, bool consoleMode)
    {
        _serviceToken = serviceToken;
        _consoleMode = consoleMode;

        if (string.IsNullOrEmpty(_options.PasswordHash) || string.IsNullOrEmpty(_options.PasswordSalt))
        {
            _logger.LogError("Parola hash/salt yapılandırılmamış. 'set-password' komutuyla ayarlayın; aksi halde tüm doğrulamalar başarısız olur.");
        }

        _logger.LogInformation(
            "OzgurAuth kapısı hazır. TestModu={TestMode} Zaman aşımı={Timeout}sn Deneme={Attempts} Konsol={Console}",
            _options.TestMode, _options.TimeoutSeconds, _options.MaxAttempts, _consoleMode);
    }

    /// <summary>Servis oturum olaylarını buraya iletir.</summary>
    public void Handle(SessionChangeDescription change)
    {
        switch (change.Reason)
        {
            case SessionChangeReason.SessionLogon:
            case SessionChangeReason.SessionUnlock:
            case SessionChangeReason.RemoteConnect:
                StartVerification(change.SessionId);
                break;

            case SessionChangeReason.SessionLogoff:
            case SessionChangeReason.RemoteDisconnect:
                Cleanup(change.SessionId);
                break;
        }
    }

    /// <summary>Konsol modunda tek bir oturum için kapıyı çalıştırır.</summary>
    public Task RunOnceAsync(int sessionId) => RunVerificationAsync(sessionId);

    public void StartVerification(int sessionId)
    {
        if (sessionId <= 0)
        {
            return; // Session 0 (hizmet oturumu) ve geçersiz numaralar atlanır
        }

        if (_active.ContainsKey(sessionId))
        {
            _logger.LogDebug("Oturum {SessionId} için doğrulama zaten etkin; yeni pencere açılmıyor.", sessionId);
            return;
        }

        _ = RunVerificationAsync(sessionId);
    }

    private async Task RunVerificationAsync(int sessionId)
    {
        var verification = new SessionVerification(sessionId, _options, _interop, _logger, _consoleMode);
        if (!_active.TryAdd(sessionId, verification))
        {
            await verification.DisposeAsync().ConfigureAwait(false);
            return;
        }

        try
        {
            await verification.RunAsync(_serviceToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Oturum {SessionId} doğrulaması beklenmeyen hata ile bitti.", sessionId);
        }
        finally
        {
            _active.TryRemove(sessionId, out _);
            await verification.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void Cleanup(int sessionId)
    {
        if (_active.TryRemove(sessionId, out SessionVerification? verification))
        {
            _logger.LogDebug("Oturum {SessionId} kapandı; doğrulama temizleniyor.", sessionId);
            verification.Cancel();
        }
    }
}
