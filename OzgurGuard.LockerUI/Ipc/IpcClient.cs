using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OzgurGuard.LockerUI.Ipc;

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
    public string Action { get; set; } = "None";

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

public sealed class IpcClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public bool IsConnected => _pipe.IsConnected;

    public IpcClient(int sessionId)
    {
        _pipe = new NamedPipeClientStream(".", $"OzgurGuard-{sessionId}", PipeDirection.InOut, PipeOptions.Asynchronous);
    }

    public async Task ConnectAsync(string token, int timeoutMs, CancellationToken ct)
    {
        await _pipe.ConnectAsync(timeoutMs, ct).ConfigureAwait(false);

        _reader = new StreamReader(_pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        _writer = new StreamWriter(_pipe, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true
        };

        // Hello mesajı gönder
        var hello = new GateMessage
        {
            Type = "hello",
            Token = token
        };
        await SendAsync(hello, ct).ConfigureAwait(false);
    }

    public async Task<GateMessage?> ReadAsync(CancellationToken ct)
    {
        if (_reader is null) return null;

        string? line = await _reader.ReadLineAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(line)) return null;

        return JsonSerializer.Deserialize<GateMessage>(line);
    }

    public async Task SendAsync(GateMessage msg, CancellationToken ct)
    {
        if (_writer is null) return;

        string json = JsonSerializer.Serialize(msg);
        await _writer.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _reader?.Dispose();
        if (_writer != null) await _writer.DisposeAsync().ConfigureAwait(false);
        await _pipe.DisposeAsync().ConfigureAwait(false);
    }
}
