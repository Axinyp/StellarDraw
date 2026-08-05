using System.IO.Pipes;
using System.Text.Json;
using WhiteBoard.Mcp.Core.Registry;
using WhiteBoard.Mcp.Core.Server;

namespace WhiteBoard.Mcp.Core.IPC;

/// <summary>
/// WPF-side named pipe server — accepts connections from WhiteBoard.McpHost and services tool calls.
/// </summary>
public sealed class WhiteboardMcpPipeServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly IMcpSessionRegistry _registry;
    private readonly IMcpToolBridge _bridge;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public WhiteboardMcpPipeServer(
        string pipeName,
        IMcpSessionRegistry registry,
        IMcpToolBridge bridge)
    {
        _pipeName = pipeName;
        _registry = registry;
        _bridge = bridge;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _acceptLoop = AcceptLoopAsync(_cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await pipe.WaitForConnectionAsync(ct);
                _ = Task.Run(() => HandleConnectionAsync(pipe, ct), ct);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync();
                break;
            }
            catch
            {
                await pipe.DisposeAsync();
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await using (pipe)
        {
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

            while (!ct.IsCancellationRequested && pipe.IsConnected)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;

                PipeMessage? response = null;
                try
                {
                    var msg = JsonSerializer.Deserialize<PipeMessage>(line);
                    response = msg switch
                    {
                        PipeListToolsRequest req => await HandleListToolsAsync(req, ct),
                        PipeInvokeToolRequest req => await HandleInvokeToolAsync(req, ct),
                        _ => null,
                    };
                }
                catch (Exception ex)
                {
                    response = new PipeInvokeToolResponse(false, "", ex.Message);
                }

                if (response is not null)
                    await writer.WriteLineAsync(JsonSerializer.Serialize(response));
            }
        }
    }

    private async Task<PipeListToolsResponse> HandleListToolsAsync(
        PipeListToolsRequest req, CancellationToken ct)
    {
        var binding = await _registry.ResolveAsync(req.Token, "stdio", ct);
        if (binding is null || !binding.SessionRef.TryGetTarget(out var session))
            return new PipeListToolsResponse([]);

        var tools = _bridge.ExportTools(session.Registry,
            new McpExportOptions(binding.Policy.AllowDestructiveTools));

        var infos = tools.Select(t => new PipeToolInfo(
            t.ProtocolTool.Name,
            t.ProtocolTool.Description ?? "",
            false,
            t.ProtocolTool.InputSchema.GetRawText())).ToList();

        return new PipeListToolsResponse(infos);
    }

    private async Task<PipeInvokeToolResponse> HandleInvokeToolAsync(
        PipeInvokeToolRequest req, CancellationToken ct)
    {
        var binding = await _registry.ResolveAsync(req.Token, "stdio", ct);
        if (binding is null || !binding.SessionRef.TryGetTarget(out var session))
            return new PipeInvokeToolResponse(false, "", "token invalid or session expired");

        try
        {
            var result = await session.Registry.InvokeAsync(req.Name, req.Args, ct);
            var json = result?.ToString() ?? "ok";
            return new PipeInvokeToolResponse(true, json, null);
        }
        catch (Exception ex)
        {
            return new PipeInvokeToolResponse(false, "", ex.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_acceptLoop is not null)
            await _acceptLoop.ConfigureAwait(false);
        _cts?.Dispose();
    }
}