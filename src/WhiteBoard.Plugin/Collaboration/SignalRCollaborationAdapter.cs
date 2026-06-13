using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;
using WhiteBoard.Contracts.Hub;
using WhiteBoard.Contracts.Models;
using WhiteBoard.Contracts.Services;

namespace WhiteBoard.Plugin.Collaboration;

/// <summary>
/// 基于 SignalR Hub 的协同适配器，实现 <see cref="ICollaborationAdapter"/>。
///
/// <b>协议约定（Hub 侧）</b>：
/// <list type="bullet">
///   <item>发送：<c>SendAllMessage(string message)</c>，message 为 JSON 序列化的 <see cref="HubEnvelope"/>。</item>
///   <item>接收：<c>ReceiveMessage(string senderId, string message)</c>。</item>
/// </list>
///
/// <b>连接管理</b>：使用 <c>WithAutomaticReconnect([0s, 2s, 5s, 30s])</c> 自动重连；
/// 连接断开期间本地事件静默丢弃（<see cref="IsActive"/> 为 false）。
///
/// <b>去重策略</b>：维护 <see cref="_seenIds"/> HashSet（上限 <see cref="SeenCap"/>），
/// 超过上限整体清空（简单 LRU 替代方案）。自身回显通过 ConnectionId 比较过滤。
///
/// <b>注意</b>：Hub 回调（OnHubMessage）由 SignalR 串行调度，HashSet 无需加锁。
/// </summary>
public sealed class SignalRCollaborationAdapter : ICollaborationAdapter, IAsyncDisposable
{
    private readonly HubConnection              _hub;
    private readonly Channel<WhiteboardEvent>   _incoming;
    private readonly HashSet<string>            _seenIds  = new(StringComparer.Ordinal);
    private readonly object                     _seenLock = new();
    private          int                        _seenCount;
    private const    int                        SeenCap   = 1_000;

    public SignalRCollaborationAdapter(SignalRAdapterOptions opts)
    {
        _incoming = Channel.CreateUnbounded<WhiteboardEvent>(
            new UnboundedChannelOptions { SingleReader = true });

        _hub = new HubConnectionBuilder()
            .WithUrl(opts.HubUrl, o =>
            {
                if (opts.AccessToken is not null)
                    o.AccessTokenProvider = () => Task.FromResult<string?>(opts.AccessToken);
            })
            .WithAutomaticReconnect([
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(30),
            ])
            .Build();

        _hub.On<string, string>("ReceiveMessage", OnHubMessage);
    }

    public bool IsActive => _hub.State == HubConnectionState.Connected;

    /// <summary>建立 Hub 连接；由插件在 InitializeAsync 中调用。</summary>
    public Task ConnectAsync(CancellationToken ct)
        => _hub.StartAsync(ct);

    // ── ICollaborationAdapter ────────────────────────────────────────────────

    public async ValueTask OnLocalEventAsync(WhiteboardEvent evt)
    {
        if (_hub.State != HubConnectionState.Connected) return;

        var env = HubEnvelopeSerializer.Wrap(evt);
        if (env is null) return;

        await _hub.SendAsync("SendAllMessage", HubEnvelopeSerializer.SerializeEnvelope(env))
                  .ConfigureAwait(false);
    }

    public async IAsyncEnumerable<WhiteboardEvent> RemoteEventsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var evt in _incoming.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return evt;
    }

    // ── Hub message handler ──────────────────────────────────────────────────

    private void OnHubMessage(string senderId, string message)
    {
        // 过滤自己发出的回显（Hub 广播包含自身）
        if (senderId == _hub.ConnectionId) return;

        var env = HubEnvelopeSerializer.DeserializeEnvelope(message);
        if (env is null) return;

        var evt = HubEnvelopeSerializer.Unwrap(env);
        if (evt is null) return;

        if (!TryMarkSeen(evt.EventId)) return;

        _incoming.Writer.TryWrite(evt);
    }

    // ── Deduplication ────────────────────────────────────────────────────────

    private bool TryMarkSeen(string eventId)
    {
        lock (_seenLock)
        {
            if (_seenIds.Contains(eventId)) return false;
            if (_seenCount >= SeenCap) { _seenIds.Clear(); _seenCount = 0; }
            _seenIds.Add(eventId);
            _seenCount++;
            return true;
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        _incoming.Writer.TryComplete();
        await _hub.DisposeAsync().ConfigureAwait(false);
    }
}
