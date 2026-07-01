using WhiteBoard.AI.Abstractions;
using WhiteBoard.AI.Session;

namespace WhiteBoard.Mcp.Core.Registry;

/// <summary>
/// 将 <see cref="IMcpSessionRegistry"/> 适配为 <see cref="IMcpTokenFactory"/>。
/// 供应用层注入 <see cref="WhiteboardAISession"/>，打破 AI ↔ Mcp.Core 循环依赖：
/// <code>
/// var factory = new McpSessionRegistryTokenFactory(registry, aiSession);
/// aiSession.SetMcpTokenFactory(factory);
/// await aiSession.IssueMcpTokenAsync();  // 触发 McpTokenIssued 事件
/// </code>
/// </summary>
public sealed class McpSessionRegistryTokenFactory : IMcpTokenFactory
{
    private readonly IMcpSessionRegistry _registry;
    private readonly WhiteboardAISession _session;

    private static readonly IReadOnlySet<string> DefaultTransports =
        new HashSet<string>(["stdio", "http"]);

    public McpSessionRegistryTokenFactory(
        IMcpSessionRegistry registry,
        WhiteboardAISession session)
    {
        _registry = registry;
        _session = session;
    }

    public async Task<string> IssueTokenAsync(
        TimeSpan? lifetime = null,
        bool allowDestructive = false,
        IReadOnlySet<string>? allowedTransports = null,
        CancellationToken ct = default)
    {
        var policy = new McpTokenPolicy(
            lifetime ?? TimeSpan.FromHours(1),
            allowDestructive,
            allowedTransports ?? DefaultTransports);

        var descriptor = await _registry.CreateTokenAsync(_session, policy, ct);
        return descriptor.Token;
    }

    public Task RevokeTokenAsync(string token, CancellationToken ct = default)
        => _registry.RevokeAsync(token, ct);
}