using System.Runtime.CompilerServices;
using WhiteBoard.Contracts.Models;

namespace WhiteBoard.Contracts.Services;

/// <summary>
/// 协同适配器接口：负责将本地白板事件广播到远端，并将远端事件流式传入本地 Session。
/// <para>
/// 实现约定：<br/>
/// - <see cref="OnLocalEventAsync"/> 不应阻塞 UI 或执行器线程（非阻塞发送或入队即返回）。<br/>
/// - <see cref="RemoteEventsAsync"/> 在 CancellationToken 取消前持续 yield，取消时正常结束。<br/>
/// - 实现类负责去重、自回显过滤，不将本机发出的事件再推回给 Session。
/// </para>
/// </summary>
public interface ICollaborationAdapter
{
    /// <summary>是否已建立活跃连接（未连接时本地事件静默丢弃）。</summary>
    bool IsActive { get; }

    /// <summary>将本地产生的事件发往远端（fire-and-forget，连接断开时静默忽略）。</summary>
    ValueTask OnLocalEventAsync(WhiteboardEvent evt);

    /// <summary>持续产出来自其他客户端的远端事件流，直到 ct 取消。</summary>
    IAsyncEnumerable<WhiteboardEvent> RemoteEventsAsync(CancellationToken ct);
}

/// <summary>
/// 空协同适配器：单机模式下使用，所有本地事件静默丢弃，远端事件流永不产出。
/// </summary>
public sealed class NullCollaborationAdapter : ICollaborationAdapter
{
    public static readonly NullCollaborationAdapter Instance = new();

    private NullCollaborationAdapter() { }

    public bool IsActive => false;

    public ValueTask OnLocalEventAsync(WhiteboardEvent evt) => default;

    public async IAsyncEnumerable<WhiteboardEvent> RemoteEventsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        yield break;
    }
}
