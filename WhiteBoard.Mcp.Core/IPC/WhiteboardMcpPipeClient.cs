using System.IO.Pipes;
using System.Text.Json;

namespace WhiteBoard.Mcp.Core.IPC;

/// <summary>
/// McpHost-side named pipe client — used by WhiteBoard.McpHost to communicate with the WPF process.
/// </summary>
public sealed class WhiteboardMcpPipeClient : IAsyncDisposable
{
    private readonly string _pipeName;
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private StreamReader? _reader;

    public WhiteboardMcpPipeClient(string pipeName) => _pipeName = pipeName;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await _pipe.ConnectAsync(TimeSpan.FromSeconds(10), ct);
        _writer = new StreamWriter(_pipe, leaveOpen: true) { AutoFlush = true };
        _reader = new StreamReader(_pipe, leaveOpen: true);
    }

    public async Task<PipeListToolsResponse> ListToolsAsync(
        PipeListToolsRequest req, CancellationToken ct = default)
    {
        var line = await SendAndReceiveAsync(req, ct);
        return JsonSerializer.Deserialize<PipeListToolsResponse>(line)
               ?? new PipeListToolsResponse([]);
    }

    public async Task<PipeInvokeToolResponse> InvokeToolAsync(
        PipeInvokeToolRequest req, CancellationToken ct = default)
    {
        var line = await SendAndReceiveAsync(req, ct);
        return JsonSerializer.Deserialize<PipeInvokeToolResponse>(line)
               ?? new PipeInvokeToolResponse(false, "", "deserialization failed");
    }

    private async Task<string> SendAndReceiveAsync(PipeMessage msg, CancellationToken ct)
    {
        if (_writer is null || _reader is null)
            throw new InvalidOperationException("Not connected");

        await _writer.WriteLineAsync(JsonSerializer.Serialize(msg));
        return await _reader.ReadLineAsync(ct) ?? "";
    }

    public async ValueTask DisposeAsync()
    {
        if (_writer is not null) await _writer.DisposeAsync();
        if (_reader is not null) _reader.Dispose();
        if (_pipe is not null) await _pipe.DisposeAsync();
    }
}
