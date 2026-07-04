using System.Security.Cryptography;
using System.Text;
using WhiteBoard.AI.Session;

namespace WhiteBoard.Mcp.Core.Registry;

public sealed class McpSessionRegistry : IMcpSessionRegistry, IDisposable
{
    private readonly ConcurrentDictionary<string, McpSessionBinding> _bindings = new();
    private readonly Timer _cleanupTimer;

    public IReadOnlyList<McpSessionBinding> ActiveBindings =>
        _bindings.Values.Where(b => !b.IsRevoked && b.ExpiresAt > DateTimeOffset.UtcNow).ToList();

    public event EventHandler<McpSessionBinding>? ConnectionOpened;

    public event EventHandler<McpSessionBinding>? ConnectionClosed;

    event EventHandler<McpSessionBinding> IMcpSessionRegistry.ConnectionOpened
    {
        add => ConnectionOpened += value;
        remove => ConnectionOpened -= value;
    }

    event EventHandler<McpSessionBinding> IMcpSessionRegistry.ConnectionClosed
    {
        add => ConnectionClosed += value;
        remove => ConnectionClosed -= value;
    }

    public McpSessionRegistry()
    {
        _cleanupTimer = new Timer(_ => Cleanup(), null,
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public Task<McpTokenDescriptor> CreateTokenAsync(
        WhiteboardAISession session,
        McpTokenPolicy policy,
        CancellationToken ct = default)
    {
        var token = GenerateToken();
        var hash = ComputeHash(token);
        var expiresAt = DateTimeOffset.UtcNow.Add(policy.Lifetime);

        var binding = new McpSessionBinding
        {
            Token = token,
            SessionRef = new WeakReference<WhiteboardAISession>(session),
            Policy = policy,
            BoundAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
        };

        _bindings[token] = binding;
        return Task.FromResult(new McpTokenDescriptor(token, hash, expiresAt, policy));
    }

    public Task<McpSessionBinding?> ResolveAsync(
        string token, string transport, CancellationToken ct = default)
    {
        if (!_bindings.TryGetValue(token, out var binding))
            return Task.FromResult<McpSessionBinding?>(null);

        if (binding.IsRevoked || binding.ExpiresAt <= DateTimeOffset.UtcNow)
            return Task.FromResult<McpSessionBinding?>(null);

        if (!binding.Policy.AllowedTransports.Contains(transport))
            return Task.FromResult<McpSessionBinding?>(null);

        if (!binding.SessionRef.TryGetTarget(out _))
            return Task.FromResult<McpSessionBinding?>(null);

        binding.LastUsedAt = DateTimeOffset.UtcNow;
        if (string.IsNullOrEmpty(binding.Transport))
            binding.Transport = transport;

        return Task.FromResult<McpSessionBinding?>(binding);
    }

    public Task RevokeAsync(string token, CancellationToken ct = default)
    {
        if (_bindings.TryGetValue(token, out var binding))
        {
            binding.IsRevoked = true;
            ConnectionClosed?.Invoke(this, binding);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 将所有仍有效（未撤销、未过期）的 Token 绑定重新指向新的会话实例。
    /// 用于设置保存时重建 <see cref="WhiteboardAISession"/> 后，把已签发的 Token 移交给新会话，
    /// 避免因旧会话被回收导致 Token 失效（<see cref="ResolveAsync"/> 会因弱引用失效而拒绝）。
    /// </summary>
    public void RebindActiveSessions(WhiteboardAISession session)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var binding in _bindings.Values)
        {
            if (!binding.IsRevoked && binding.ExpiresAt > now)
                binding.SessionRef = new WeakReference<WhiteboardAISession>(session);
        }
    }

    private void Cleanup()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, binding) in _bindings)
        {
            if (binding.IsRevoked || binding.ExpiresAt <= now
                || !binding.SessionRef.TryGetTarget(out _))
            {
                _bindings.TryRemove(key, out _);
            }
        }
    }

    private static string GenerateToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
           .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string ComputeHash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public void Dispose() => _cleanupTimer.Dispose();
}