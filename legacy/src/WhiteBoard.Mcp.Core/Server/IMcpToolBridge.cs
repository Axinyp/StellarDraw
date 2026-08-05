using ModelContextProtocol.Server;
using WhiteBoard.AI.Tools;

namespace WhiteBoard.Mcp.Core.Server;

public interface IMcpToolBridge
{
    IReadOnlyList<McpServerTool> ExportTools(
        WhiteboardToolRegistry registry,
        McpExportOptions options);
}