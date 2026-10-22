using System.IO;
using System.IO.Pipes;
using System.Text;

namespace OzgurAuth.VerifierUI.Ipc;

/// <summary>
/// Servisin oturuma özel NamedPipe sunucusuna bağlanan istemci. İlk mesaj olarak
/// tek kullanımlık token içeren "hello" gönderir.
/// </summary>
public sealed class GatePipeClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public GatePipeClient(int sessionId)
    {
        _stream = new NamedPipeClientStream(
            ".",
            $"OzgurAuth.Gate.{sessionId}",
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
    }

    public async Task ConnectAsync(string token, int timeoutMs, CancellationToken ct)
    {
        await _stream.ConnectAsync(timeoutMs, ct).ConfigureAwait(false);
        _reader = new StreamReader(_stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        _writer = new StreamWriter(_stream, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };

        await SendAsync(new GateMessage { Type = "hello", Token = token }, ct).ConfigureAwait(false);
    }

    public async Task SendPasswordAsync(string password, CancellationToken ct)
        => await SendAsync(new GateMessage { Type = "verify", Password = password }, ct).ConfigureAwait(false);

    public async Task SendAsync(GateMessage message, CancellationToken ct)
    {
        if (_writer is null)
        {
            return;
        }

        await _writer.WriteLineAsync(message.Serialize().AsMemory(), ct).ConfigureAwait(false);
    }

    public async Task<GateMessage?> ReadAsync(CancellationToken ct)
    {
        if (_reader is null)
        {
            return null;
        }

        string? line = await _reader.ReadLineAsync(ct).ConfigureAwait(false);
        return line is null ? null : GateMessage.Deserialize(line);
    }

    public bool IsConnected => _stream.IsConnected;

    public async ValueTask DisposeAsync()
    {
        try
        {
            _writer?.Dispose();
            _reader?.Dispose();
        }
        catch
        {
            // yok say
        }

        await _stream.DisposeAsync().ConfigureAwait(false);
    }
}
