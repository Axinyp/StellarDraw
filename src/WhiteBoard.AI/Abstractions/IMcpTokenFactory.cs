namespace WhiteBoard.AI.Abstractions;

/// <summary>
/// MCP Token 签发抽象。由 WhiteBoard.Mcp.Core 实现（McpSessionRegistryTokenFactory），
/// 通过此接口注入 WhiteboardAISession，打破 AI ↔ Mcp.Core 循环依赖。
/// </summary>
public interface IMcpTokenFactory
{
    /// <summary>
    /// 签发新 Token。若已有 Token，实现层应先撤销旧 Token。
    /// </summary>
    Task<string> IssueTokenAsync(
        TimeSpan?             lifetime          = null,
        bool                  allowDestructive  = false,
        IReadOnlySet<string>? allowedTransports = null,
        CancellationToken     ct                = default);

    /// <summary>撤销指定 Token。</summary>
    Task RevokeTokenAsync(string token, CancellationToken ct = default);
}
