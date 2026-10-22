using System.Text.Json;
using System.Text.Json.Nodes;
using OzgurAuth.Service.Security;

namespace OzgurAuth.Service;

/// <summary>
/// "set-password" CLI komutu: yeni parolanın PBKDF2 salt + hash karşılığını hesaplar
/// ve exe'nin yanındaki appsettings.json içine yazar. Düz parola diske yazılmaz.
/// </summary>
internal static class PasswordTool
{
    public static int Run(string[] args)
    {
        string? password = args.Length > 1 ? args[1] : null;

        if (string.IsNullOrEmpty(password))
        {
            Console.Write("Yeni parola: ");
            password = ReadHidden();
            Console.Write("Parola (tekrar): ");
            string confirm = ReadHidden();
            if (password != confirm)
            {
                Console.Error.WriteLine("Parolalar eşleşmedi.");
                return 1;
            }
        }

        if (string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("Boş parola kabul edilmez.");
            return 1;
        }

        (string salt, string hash) = PasswordVerifier.CreateHash(password);

        string settingsPath = Path.Combine(
            Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory,
            "appsettings.json");

        try
        {
            JsonObject root = File.Exists(settingsPath)
                ? JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject ?? new JsonObject()
                : new JsonObject();

            if (root["Gate"] is not JsonObject gate)
            {
                gate = new JsonObject();
                root["Gate"] = gate;
            }

            gate["PasswordSalt"] = salt;
            gate["PasswordHash"] = hash;

            File.WriteAllText(settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Parola güncellendi: {settingsPath}");
            Console.WriteLine("Değişikliğin etkili olması için hizmeti yeniden başlatın (Restart-Service OzgurAuth).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"appsettings.json yazılamadı: {ex.Message}");
            return 1;
        }
    }

    private static string ReadHidden()
    {
        var buffer = new System.Text.StringBuilder();
        ConsoleKeyInfo key;
        while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0) buffer.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }

        Console.WriteLine();
        return buffer.ToString();
    }
}
