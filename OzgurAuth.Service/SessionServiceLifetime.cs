using System.Runtime.Versioning;
using System.ServiceProcess;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OzgurAuth.Service;

/// <summary>
/// WindowsServiceLifetime'ın oturum-değişikliği duyarlı türevi. Hazır lifetime
/// CanHandleSessionChangeEvent açmadığı ve OnSessionChange vermediği için bu küçük
/// türev yazıldı. Servis standart biçimde (SCM / Stop-Service) durdurulabilir.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SessionServiceLifetime : ServiceBase, IHostLifetime
{
    private readonly TaskCompletionSource<object?> _delayStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim _delayStop = new();
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly SessionGate _gate;
    private readonly ILogger<SessionServiceLifetime> _logger;

    public SessionServiceLifetime(
        IHostApplicationLifetime applicationLifetime,
        SessionGate gate,
        ILogger<SessionServiceLifetime> logger)
    {
        _applicationLifetime = applicationLifetime;
        _gate = gate;
        _logger = logger;

        ServiceName = "OzgurAuth";
        CanHandleSessionChangeEvent = true;
        CanStop = true;
        CanShutdown = true;
    }

    public Task WaitForStartAsync(CancellationToken cancellationToken)
    {
        _gate.Initialize(_applicationLifetime.ApplicationStopping, consoleMode: false);

        cancellationToken.Register(() => _delayStart.TrySetCanceled());
        _applicationLifetime.ApplicationStopping.Register(Stop);

        var thread = new Thread(Run) { IsBackground = true };
        thread.Start();

        return _delayStart.Task;
    }

    private void Run()
    {
        try
        {
            Run(this); // SCM'ye bağlanana kadar bloklar
            _delayStart.TrySetException(new InvalidOperationException("Hizmet başlatılmadan durdu."));
            _delayStop.Wait();
        }
        catch (Exception ex)
        {
            _delayStart.TrySetException(ex);
        }
    }

    protected override void OnStart(string[] args)
    {
        _delayStart.TrySetResult(null);
        base.OnStart(args);
    }

    protected override void OnStop()
    {
        _applicationLifetime.StopApplication();
        _delayStop.Set();
        base.OnStop();
    }

    protected override void OnShutdown()
    {
        _applicationLifetime.StopApplication();
        _delayStop.Set();
        base.OnShutdown();
    }

    protected override void OnSessionChange(SessionChangeDescription changeDescription)
    {
        try
        {
            _gate.Handle(changeDescription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OnSessionChange işlenirken hata. Reason={Reason} Session={SessionId}",
                changeDescription.Reason, changeDescription.SessionId);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Stop();
        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _delayStop.Set();
            _delayStop.Dispose();
        }

        base.Dispose(disposing);
    }
}
