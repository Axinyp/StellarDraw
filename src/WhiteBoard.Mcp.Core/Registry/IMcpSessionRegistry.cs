using WhiteBoard.AI.Session;

namespace WhiteBoard.Mcp.Core.Registry;

public interface IMcpSessionRegistry
{
    Task<McpTokenDescriptor> CreateTokenAsync(
        WhiteboardAISession session,
        McpTokenPolicy policy,
        CancellationToken ct = default);

    Task<McpSessionBinding?> ResolveAsync(
        string token, string transport, CancellationToken ct = default);

    Task RevokeAsync(string token, CancellationToken ct = default);

    IReadOnlyList<McpSessionBinding> ActiveBindings { get; }

    event EventHandler<McpSessionBinding> ConnectionOpened;

    event EventHandler<McpSessionBinding> ConnectionClosed;
}

public sealed record McpTokenDescriptor(
    string Token,
    string TokenHash,
    DateTimeOffset ExpiresAt,
    McpTokenPolicy Policy);

public sealed record McpTokenPolicy(
    TimeSpan Lifetime,
    bool AllowDestructiveTools,
    IReadOnlySet<string> AllowedTransports);

public sealed class McpSessionBinding
{
    public string Token { get; init; } = "";
    /// <summary>可重绑：设置保存重建会话时，把绑定移交给新会话实例，保持 Token 不变。</summary>
    public WeakReference<WhiteboardAISession> SessionRef { get; set; } = null!;
    public McpTokenPolicy Policy { get; init; } = null!;
    public DateTimeOffset BoundAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public string Transport { get; set; } = "";
    public DateTimeOffset? LastUsedAt { get; set; }
    public bool IsRevoked { get; set; }
}