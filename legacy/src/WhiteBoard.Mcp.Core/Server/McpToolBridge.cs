using ModelContextProtocol.Server;
using WhiteBoard.AI.Tools;

namespace WhiteBoard.Mcp.Core.Server;

public sealed class McpToolBridge : IMcpToolBridge
{
    public IReadOnlyList<McpServerTool> ExportTools(
        WhiteboardToolRegistry registry,
        McpExportOptions options)
    {
        var result = new List<McpServerTool>();

        foreach (var fn in registry.GetFunctions())
        {
            var cap = registry.GetCapability(fn.Name);
            if (cap?.IsDestructive == true && !options.IncludeDestructiveTools)
                continue;

            var toolName = options.PrefixToolNames
                ? $"{options.ToolNamePrefix}__{fn.Name}"
                : fn.Name;

            result.Add(new WhiteboardMcpServerTool(fn, toolName));
        }

        return result;
    }
}