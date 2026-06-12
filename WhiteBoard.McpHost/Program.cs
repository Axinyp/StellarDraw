using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WhiteBoard.Mcp.Core.IPC;
using WhiteBoard.Mcp.Core.Server;
using WhiteBoard.McpHost;

var token    = Environment.GetEnvironmentVariable("WHITEBOARD_MCP_TOKEN") ?? "";
var pipeName = Environment.GetEnvironmentVariable("WHITEBOARD_MCP_PIPE_NAME") ?? "whiteboard-mcp";

if (string.IsNullOrEmpty(token))
{
    Console.Error.WriteLine("[McpHost] WHITEBOARD_MCP_TOKEN not set");
    return 1;
}

// Connect to WPF process via named pipe
await using var pipeClient = new WhiteboardMcpPipeClient(pipeName);
try
{
    await pipeClient.ConnectAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[McpHost] Failed to connect to WPF pipe '{pipeName}': {ex.Message}");
    return 2;
}

// Fetch tool list from WPF process
var toolList = await pipeClient.ListToolsAsync(new PipeListToolsRequest(token));
if (toolList.Tools.Count == 0)
{
    Console.Error.WriteLine("[McpHost] No tools available (check token validity)");
    return 3;
}

// Build MCP server tools backed by the named pipe
var mcpTools = toolList.Tools
    .Select(t => new WhiteboardMcpServerTool(
        new McpPipeBackedAiFunction(pipeClient, token, t),
        t.Name))
    .ToList<McpServerTool>();

var host = Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithTools(mcpTools);
    })
    .Build();

await host.RunAsync();
return 0;
