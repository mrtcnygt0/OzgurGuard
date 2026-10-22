using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace OzgurAuth.Service.Ipc;

/// <summary>
/// Tek bir oturum için NamedPipe sunucusu. Boru adı oturum numarasına bağlıdır
/// ve erişim yalnızca LocalSystem ile o oturumun kullanıcısına açılır.
/// </summary>
public sealed class GatePipeServer : IAsyncDisposable
{
    public static string PipeNameFor(int sessionId) => $"OzgurAuth.Gate.{sessionId}";

    private readonly NamedPipeServerStream _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    private GatePipeServer(NamedPipeServerStream stream) => _stream = stream;

    /// <summary>
    /// Boruyu oluşturur. userSid verilirse (servis modu) ACL o kullanıcıya + SYSTEM'e
    /// kısıtlanır; null ise (konsol/test modu) varsayılan güvenlik kullanılır.
    /// </summary>
    public static GatePipeServer Create(int sessionId, SecurityIdentifier? userSid)
    {
        string pipeName = PipeNameFor(sessionId);

        if (OperatingSystem.IsWindows() && userSid is not null)
        {
            var security = new PipeSecurity();
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            security.AddAccessRule(new PipeAccessRule(system, PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(userSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));

            var stream = NamedPipeServerStreamAcl.Create(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                0, 0,
                security);
            return new GatePipeServer(stream);
        }

        var plain = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        return new GatePipeServer(plain);
    }

    public async Task WaitForConnectionAsync(CancellationToken ct)
    {
        await _stream.WaitForConnectionAsync(ct).ConfigureAwait(false);
        _reader = new StreamReader(_stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        _writer = new StreamWriter(_stream, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
    }

    public async Task<GateMessage?> ReadMessageAsync(CancellationToken ct)
    {
        if (_reader is null)
        {
            return null;
        }

        string? line = await _reader.ReadLineAsync(ct).ConfigureAwait(false);
        return line is null ? null : GateMessage.Deserialize(line);
    }

    public async Task SendMessageAsync(GateMessage message, CancellationToken ct)
    {
        if (_writer is null)
        {
            return;
        }

        await _writer.WriteLineAsync(message.Serialize().AsMemory(), ct).ConfigureAwait(false);
    }

    public bool IsConnected => _stream.IsConnected;

    public async ValueTask DisposeAsync()
    {
        try
        {
            _writer?.Dispose();
            _reader?.Dispose();
            if (_stream.IsConnected)
            {
                _stream.Disconnect();
            }
        }
        catch
        {
            // kapatma sırasındaki hatalar yok sayılır
        }

        await _stream.DisposeAsync().ConfigureAwait(false);
    }
}
