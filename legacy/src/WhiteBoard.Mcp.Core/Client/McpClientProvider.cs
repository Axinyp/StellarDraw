using ModelContextProtocol.Client;
using System.Diagnostics;

namespace WhiteBoard.Mcp.Core.Client;

public sealed class McpClientProvider : IMcpClientProvider
{
    private readonly List<(McpClient client, ExternalMcpConnectionSpec spec)> _clients = [];

    public async ValueTask InitializeAsync(
        IReadOnlyList<ExternalMcpConnectionSpec> connections,
        CancellationToken ct = default)
    {
        foreach (var conn in connections.Where(c => c.Enabled))
        {
            try
            {
                IClientTransport transport = conn.Transport switch
                {
                    "stdio" => new StdioClientTransport(
                        new StdioClientTransportOptions
                        {
                            Command = conn.Command,
                            Arguments = conn.Arguments,
                            Name = conn.DisplayName,
                        }),
                    "http" => new HttpClientTransport(
                        new HttpClientTransportOptions
                        {
                            Endpoint = new Uri(conn.Endpoint),
                            Name = conn.DisplayName,
                        }),
                    _ => throw new NotSupportedException($"Transport '{conn.Transport}' not supported"),
                };

                var client = await McpClient.CreateAsync(transport, cancellationToken: ct);
                _clients.Add((client, conn));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MCP] Client '{conn.DisplayName}' init failed: {ex.Message}");
            }
        }
    }

    public async ValueTask<IReadOnlyList<ExternalMcpToolRegistration>> GetToolRegistrationsAsync(
        CancellationToken ct = default)
    {
        var result = new List<ExternalMcpToolRegistration>();

        foreach (var (client, spec) in _clients)
        {
            try
            {
                var tools = await client.ListToolsAsync(cancellationToken: ct);
                foreach (var tool in tools)
                {
                    var prefixed = tool.WithName($"{spec.ToolNamePrefix}__{tool.Name}");
                    var policy = new ExternalMcpToolPolicy(
                        RequiresConfirmation: spec.TrustLevel == "restricted",
                        IsDestructive: false,
                        AllowInAutoApproveMode: spec.TrustLevel == "trusted");
                    result.Add(new(spec.ConnectionId, tool.Name, prefixed.Name, prefixed, policy));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MCP] ListTools failed for '{spec.DisplayName}': {ex.Message}");
            }
        }

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (client, _) in _clients)
            await client.DisposeAsync();
        _clients.Clear();
    }
}