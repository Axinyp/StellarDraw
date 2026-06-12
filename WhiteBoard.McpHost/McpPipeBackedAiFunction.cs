using System.Text.Json;
using Microsoft.Extensions.AI;
using WhiteBoard.Mcp.Core.IPC;

namespace WhiteBoard.McpHost;

/// <summary>
/// AIFunction adapter that proxies tool invocations through the named pipe to the WPF process.
/// </summary>
public sealed class McpPipeBackedAiFunction : AIFunction
{
    private readonly WhiteboardMcpPipeClient _pipe;
    private readonly string                 _token;
    private readonly PipeToolInfo           _info;

    public McpPipeBackedAiFunction(
        WhiteboardMcpPipeClient pipe,
        string                  token,
        PipeToolInfo            info)
    {
        _pipe  = pipe;
        _token = token;
        _info  = info;
    }

    public override string Name        => _info.Name;
    public override string Description => _info.Description;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments? arguments,
        CancellationToken    cancellationToken)
    {
        var args = new Dictionary<string, object?>();
        if (arguments is not null)
            foreach (var (k, v) in arguments)
                args[k] = v;

        var req      = new PipeInvokeToolRequest(_token, _info.Name, args);
        var response = await _pipe.InvokeToolAsync(req, cancellationToken);

        if (!response.Success)
            throw new InvalidOperationException(response.Error ?? "Tool invocation failed");

        return response.Json;
    }
}
