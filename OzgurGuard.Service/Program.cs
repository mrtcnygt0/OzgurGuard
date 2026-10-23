using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OzgurGuard.Service;
using OzgurGuard.Service.Security;
using OzgurGuard.Service.Services;

// CLI: set-password komutu
if (args.Length > 0 && string.Equals(args[0], "set-password", StringComparison.OrdinalIgnoreCase))
{
    string password = args.Length > 1 ? args[1] : string.Empty;

    if (string.IsNullOrWhiteSpace(password))
    {
        Console.Write("Yeni OzgurGuard parolası: ");
        password = Console.ReadLine() ?? string.Empty;
    }

    if (string.IsNullOrWhiteSpace(password))
    {
        Console.Error.WriteLine("[HATA] Boş parola kabul edilmez.");
        return 1;
    }

    (string salt, string hash) = PasswordManager.CreateHash(password);

    string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

    try
    {
        JsonObject root = File.Exists(settingsPath)
            ? JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject ?? new JsonObject()
            : new JsonObject();

        if (root["OzgurGuard"] is not JsonObject guardSection)
        {
            guardSection = new JsonObject();
            root["OzgurGuard"] = guardSection;
        }

        guardSection["PasswordSalt"] = salt;
        guardSection["PasswordHash"] = hash;

        File.WriteAllText(settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("[BAŞARILI] Parola PBKDF2 (SHA-256, 100.000 iterasyon) ile hashlenerek appsettings.json içine kaydedildi.");
        Console.WriteLine("Değişikliğin uygulanması için servisi yeniden başlatın: Restart-Service OzgurGuard");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[HATA] appsettings.json güncellenemedi: {ex.Message}");
        return 1;
    }
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "OzgurGuard";
});

builder.Services.Configure<GuardOptions>(builder.Configuration.GetSection(GuardOptions.SectionName));
builder.Services.AddHostedService<SessionWatcherService>();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

if (OperatingSystem.IsWindows())
{
    builder.Logging.AddEventLog(settings =>
    {
        settings.SourceName = "OzgurGuard";
        settings.LogName = "Application";
    });
}

IHost host = builder.Build();
await host.RunAsync();
return 0;
