using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace WhiteBoard.Mcp.Core.Server;

/// <summary>
/// Public McpServerTool subclass wrapping an AIFunction.
/// Replaces the internal AIFunctionMcpServerTool from the MCP SDK.
/// </summary>
public sealed class WhiteboardMcpServerTool : McpServerTool
{
    private readonly AIFunction _fn;
    private readonly Tool _protocolTool;

    public WhiteboardMcpServerTool(AIFunction fn, string toolName)
    {
        _fn = fn;
        _protocolTool = new Tool
        {
            Name = toolName,
            Description = fn.Description,
            InputSchema = BuildSchema(fn),
        };
    }

    public override Tool ProtocolTool => _protocolTool;

    public override IReadOnlyList<object> Metadata => [];

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        var args = request.Params?.Arguments?
            .ToDictionary(k => k.Key, v => (object?)v.Value)
            ?? new Dictionary<string, object?>();

        try
        {
            var fnArgs = new AIFunctionArguments(args);
            var result = await _fn.InvokeAsync(fnArgs, cancellationToken);
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = result?.ToString() ?? "" }],
            };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = ex.Message }],
                IsError = true,
            };
        }
    }

    private static JsonElement BuildSchema(AIFunction fn)
    {
        try
        {
            var s = fn.JsonSchema;
            if (s.ValueKind != JsonValueKind.Undefined)
                return s;
        }
        catch { /* JsonSchema not implemented by this AIFunction type */ }

        return JsonSerializer.SerializeToElement(new { type = "object" });
    }
}