using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OzgurAuth.Service;
using OzgurAuth.Service.Native;

// set-password komutu host kurmadan çalışır.
if (args.Length > 0 && string.Equals(args[0], "set-password", StringComparison.OrdinalIgnoreCase))
{
    return PasswordTool.Run(args);
}

bool consoleMode = args.Contains("--console", StringComparer.OrdinalIgnoreCase);

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<GateOptions>(builder.Configuration.GetSection(GateOptions.SectionName));
builder.Services.AddSingleton<SessionInterop>();
builder.Services.AddSingleton<SessionGate>();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
// Event Log yalnızca hizmet modunda; konsol testinde kaynak henüz kayıtlı olmayabilir.
if (!consoleMode && OperatingSystem.IsWindows())
{
    builder.Logging.AddEventLog(settings =>
    {
        settings.SourceName = "OzgurAuth";
        settings.LogName = "Application";
    });
}

if (!consoleMode && OperatingSystem.IsWindows())
{
    // Windows hizmeti: oturum olaylarını dinleyen özel lifetime.
    builder.Services.AddSingleton<IHostLifetime, SessionServiceLifetime>();
}

IHost host = builder.Build();

if (consoleMode)
{
    var gate = host.Services.GetRequiredService<SessionGate>();
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
    gate.Initialize(lifetime.ApplicationStopping, consoleMode: true);

    int sessionId = Process.GetCurrentProcess().SessionId;
    Console.WriteLine($"[OzgurAuth] Konsol modu. Oturum {sessionId} için doğrulama arayüzü açılıyor (kesme daima simüle edilir).");

    await gate.RunOnceAsync(sessionId);
    Console.WriteLine("[OzgurAuth] Konsol modu doğrulama akışı bitti.");
    return 0;
}

await host.RunAsync();
return 0;
