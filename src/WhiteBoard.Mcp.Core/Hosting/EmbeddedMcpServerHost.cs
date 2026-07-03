using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using WhiteBoard.Mcp.Core.Registry;
using WhiteBoard.Mcp.Core.Server;

namespace WhiteBoard.Mcp.Core.Hosting;

public sealed class EmbeddedMcpServerOptions
{
    public string Url { get; set; } = "http://127.0.0.1:31888";
    public string RoutePrefix { get; set; } = "/mcp";
    public bool EnableLegacySse { get; set; } = false;
}

/// <summary>
/// Kestrel-embedded MCP HTTP server running in-process alongside the WPF app.
/// Each connecting MCP client authenticates with a bearer token, which is resolved
/// to a live WhiteboardAISession and per-session tool collection.
/// </summary>
public sealed class EmbeddedMcpServerHost : IAsyncDisposable
{
    private WebApplication? _app;

    public async ValueTask StartAsync(
        EmbeddedMcpServerOptions opts,
        IMcpSessionRegistry registry,
        IMcpToolBridge bridge,
        CancellationToken ct = default)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseSetting("urls", opts.Url);

        builder.Services
            .AddSingleton(registry)
            .AddSingleton(bridge)
            .AddAuthentication("McpToken")
            .AddScheme<McpTokenAuthOptions, McpTokenAuthHandler>("McpToken", _ => { });

        builder.Services.AddAuthorization();

        builder.Services
            .AddMcpServer()
            .WithHttpTransport(o =>
            {
                o.EnableLegacySse = opts.EnableLegacySse;
                o.ConfigureSessionOptions = async (ctx, mcpOpts, sessionCt) =>
                {
                    var token = ExtractBearerToken(ctx);
                    if (string.IsNullOrEmpty(token)) return;

                    var binding = await registry.ResolveAsync(token, "http", sessionCt);
                    if (binding is null || !binding.SessionRef.TryGetTarget(out var session))
                        return;

                    var tools = bridge.ExportTools(session.Registry,
                        new McpExportOptions(binding.Policy.AllowDestructiveTools));

                    // 必须显式初始化 tools capability 与集合：否则 ToolCollection 为 null，
                    // 上面的 ?.Add 被短路、工具不注册，server 也不声明 tools capability，
                    // 客户端 tools/list 会收到 -32601 (Method not available)。
                    mcpOpts.Capabilities ??= new ServerCapabilities();
                    mcpOpts.Capabilities.Tools ??= new ToolsCapability();
                    mcpOpts.ToolCollection ??= new();
                    foreach (var tool in tools)
                        mcpOpts.ToolCollection.Add(tool);
                };
            });

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapMcp(opts.RoutePrefix).RequireAuthorization();

        await _app.StartAsync(ct);
    }

    private static string ExtractBearerToken(HttpContext ctx)
    {
        var header = ctx.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : "";
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }
}