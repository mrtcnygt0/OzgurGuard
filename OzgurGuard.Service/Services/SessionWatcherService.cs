using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OzgurGuard.Service.Native;
using OzgurGuard.Service.Security;

namespace OzgurGuard.Service.Services;

public sealed class SessionWatcherService : BackgroundService
{
    private readonly GuardOptions _options;
    private readonly ILogger<SessionWatcherService> _logger;
    private readonly SecurityLogger _secLogger;

    private readonly ConcurrentDictionary<int, SessionWatchdog> _activeWatchdogs = new();
    private readonly ConcurrentDictionary<int, bool> _verifiedSessions = new();

    public SessionWatcherService(
        IOptions<GuardOptions> options,
        ILogger<SessionWatcherService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _secLogger = new SecurityLogger(logger, _options.LogDirectory);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "OzgurGuard.Service başlatıldı. Windows Server RDP Güvenlik Katmanı Aktif. DevMode={DevMode}",
            _options.DevelopmentMode);

        _secLogger.LogEvent(
            "INFO",
            0,
            "SYSTEM",
            "127.0.0.1",
            "SERVICE_STARTED",
            "OzgurGuard arka plan Windows servisi Session 0'da SYSTEM yetkisiyle çalışıyor. IIS ve Web portları engellenmez.");

        // 500ms aktif oturum izleme döngüsü (RDP, Konsol veya kilit açma anında yakalanır)
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var activeSessionIds = NativeMethods.GetActiveSessionIds();

                // 1. Yeni bağlanan / kilit açan aktif oturumları tespit et
                foreach (int sessionId in activeSessionIds)
                {
                    if (sessionId <= 0) continue; // Session 0 atlanır

                    // Bu oturum doğrulanmadıysa ve şu an kilit süreci aktif değilse başlat
                    if (!_verifiedSessions.ContainsKey(sessionId) && !_activeWatchdogs.ContainsKey(sessionId))
                    {
                        _logger.LogInformation("Yeni aktif oturum tespit edildi: Oturum={SessionId}. Kilit ekranı başlatılıyor...", sessionId);
                        StartSessionWatchdog(sessionId);
                    }
                }

                // 2. Kapanmış veya bağlantısı kesilmiş oturumları temizle (tekrar bağlanırsa yeniden kilitlensin)
                var deadSessions = _verifiedSessions.Keys.Where(id => !activeSessionIds.Contains(id)).ToList();
                foreach (int id in deadSessions)
                {
                    _verifiedSessions.TryRemove(id, out _);
                    StopSessionWatchdog(id);
                }

                await Task.Delay(500, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Oturum izleme döngüsünde beklenmeyen hata.");
            }
        }
    }

    private void StartSessionWatchdog(int sessionId)
    {
        if (_activeWatchdogs.ContainsKey(sessionId))
        {
            return;
        }

        var watchdog = new SessionWatchdog(sessionId, _options, _secLogger);
        if (!_activeWatchdogs.TryAdd(sessionId, watchdog))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await watchdog.StartAsync(CancellationToken.None).ConfigureAwait(false);

                // Eğer kilit başarıyla doğrulandıysa bu oturuma erişim izni ver
                if (watchdog.IsSucceeded)
                {
                    _verifiedSessions[sessionId] = true;
                    _logger.LogInformation("Oturum {SessionId} başarıyla doğrulandı ve yetkilendirildi.", sessionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Oturum {SessionId} kilit akışında hata.", sessionId);
            }
            finally
            {
                _activeWatchdogs.TryRemove(sessionId, out _);
                await watchdog.DisposeAsync().ConfigureAwait(false);
            }
        });
    }

    private void StopSessionWatchdog(int sessionId)
    {
        if (_activeWatchdogs.TryRemove(sessionId, out SessionWatchdog? watchdog))
        {
            _ = watchdog.DisposeAsync();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("OzgurGuard.Service durduruluyor...");

        foreach (var kvp in _activeWatchdogs)
        {
            await kvp.Value.DisposeAsync().ConfigureAwait(false);
        }
        _activeWatchdogs.Clear();
        _verifiedSessions.Clear();

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
