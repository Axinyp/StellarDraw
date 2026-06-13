namespace WhiteBoard.Mcp.Core.Server;

public sealed record McpExportOptions(
    bool IncludeDestructiveTools = false,
    bool PrefixToolNames = false,
    string ToolNamePrefix = "wb");
