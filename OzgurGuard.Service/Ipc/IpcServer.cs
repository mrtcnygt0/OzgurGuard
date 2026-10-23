using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace OzgurGuard.Service.Ipc;

public sealed class IpcServer : IAsyncDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;

    public static string GetPipeName(int sessionId) => $"OzgurGuard-{sessionId}";

    private IpcServer(NamedPipeServerStream pipe)
    {
        _pipe = pipe;
        _reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        _writer = new StreamWriter(pipe, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true
        };
    }

    public static IpcServer Create(int sessionId)
    {
        string pipeName = GetPipeName(sessionId);

        var ps = new PipeSecurity();
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        ps.AddAccessRule(new PipeAccessRule(everyone, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        ps.AddAccessRule(new PipeAccessRule(systemSid, PipeAccessRights.FullControl, AccessControlType.Allow));

        var pipe = NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 4096,
            outBufferSize: 4096,
            ps);

        return new IpcServer(pipe);
    }

    public Task WaitForConnectionAsync(CancellationToken ct) => _pipe.WaitForConnectionAsync(ct);

    public async Task<GateMessage?> ReadMessageAsync(CancellationToken ct)
    {
        string? line = await _reader.ReadLineAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(line)) return null;

        return JsonSerializer.Deserialize<GateMessage>(line);
    }

    public async Task SendMessageAsync(GateMessage msg, CancellationToken ct)
    {
        string json = JsonSerializer.Serialize(msg);
        await _writer.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _reader.Dispose();
        await _writer.DisposeAsync().ConfigureAwait(false);
        await _pipe.DisposeAsync().ConfigureAwait(false);
    }
}
