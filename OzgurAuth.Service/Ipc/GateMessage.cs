using System.Text.Json;
using System.Text.Json.Serialization;

namespace OzgurAuth.Service.Ipc;

/// <summary>
/// Servis ile doğrulama arayüzü arasındaki satır bazlı JSON protokolünün tek mesaj tipi.
/// Her mesaj tek satır JSON'dur ve newline ile biter.
/// </summary>
public sealed class GateMessage
{
    /// <summary>hello | challenge | verify | result</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>hello mesajında arayüzün taşıdığı tek kullanımlık token.</summary>
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    /// <summary>verify mesajında girilen parola.</summary>
    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("remainingSeconds")]
    public int RemainingSeconds { get; set; }

    [JsonPropertyName("attemptsRemaining")]
    public int AttemptsRemaining { get; set; }

    [JsonPropertyName("maxAttempts")]
    public int MaxAttempts { get; set; }

    [JsonPropertyName("testMode")]
    public bool TestMode { get; set; }

    [JsonPropertyName("userName")]
    public string? UserName { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>none | close | disconnected — arayüzün ne yapması gerektiği.</summary>
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Serialize() => JsonSerializer.Serialize(this, Options);

    public static GateMessage? Deserialize(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<GateMessage>(line, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Arayüzün action alanında alabileceği değerler.</summary>
public static class GateActions
{
    public const string None = "none";
    public const string Close = "close";
    public const string Disconnected = "disconnected";
}
