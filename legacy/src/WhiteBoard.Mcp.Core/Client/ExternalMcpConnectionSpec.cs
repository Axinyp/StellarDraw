namespace WhiteBoard.Mcp.Core.Client;

public sealed class ExternalMcpConnectionSpec
{
    public string ConnectionId { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Transport { get; set; } = "stdio";
    public string Command { get; set; } = "";
    public List<string> Arguments { get; set; } = [];
    public string WorkingDirectory { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string ToolNamePrefix { get; set; } = "ext";
    public string TrustLevel { get; set; } = "restricted";
    public bool AllowDestructiveTools { get; set; } = false;
}