using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace OzgurGuard.Service.Security;

public sealed class SecurityLogger
{
    private readonly ILogger _logger;
    private readonly string _logFilePath;
    private readonly object _lock = new();

    public SecurityLogger(ILogger logger, string logDirectory)
    {
        _logger = logger;
        try
        {
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }
            _logFilePath = Path.Combine(logDirectory, "security.log");
        }
        catch
        {
            _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "security.log");
        }
    }

    public void LogEvent(string level, int sessionId, string user, string clientIp, string action, string details)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string line = $"[{timestamp}] [{level.ToUpperInvariant()}] [Oturum:{sessionId}] [Kullanici:{user}] [IP:{clientIp}] [Eylem:{action}] - {details}";

        // 1. Console / Service Logger
        if (level.Equals("ERROR", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(line);
        }
        else if (level.Equals("WARN", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(line);
        }
        else
        {
            _logger.LogInformation(line);
        }

        // 2. Windows Event Log
        try
        {
            if (OperatingSystem.IsWindows())
            {
                EventLogEntryType type = level.ToUpperInvariant() switch
                {
                    "ERROR" => EventLogEntryType.Error,
                    "WARN" => EventLogEntryType.Warning,
                    _ => EventLogEntryType.Information
                };
                EventLog.WriteEntry("OzgurGuard", line, type, 100);
            }
        }
        catch
        {
            // Event log yazma izni henüz oluşturulmamış olabilir
        }

        // 3. Local Audit File: C:\ProgramData\OzgurGuard\security.log
        try
        {
            lock (_lock)
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Dosya kilitli veya erişilemezse uygulamanın çökmesini engelle
        }
    }
}
