using System.Text.Json.Serialization;

namespace OzgurGuard.Service.Ipc;

public sealed class GateMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("action")]
    public string Action { get; set; } = "None"; // None, Close, Disconnect

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("remainingSeconds")]
    public int RemainingSeconds { get; set; }

    [JsonPropertyName("attemptsRemaining")]
    public int AttemptsRemaining { get; set; }

    [JsonPropertyName("maxAttempts")]
    public int MaxAttempts { get; set; }

    [JsonPropertyName("developmentMode")]
    public bool DevelopmentMode { get; set; }

    [JsonPropertyName("userName")]
    public string? UserName { get; set; }
}

public static class GateActions
{
    public const string None = "None";
    public const string Close = "Close";
    public const string Disconnect = "Disconnect";
}
