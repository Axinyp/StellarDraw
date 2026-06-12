namespace WhiteBoard.Contracts.Plugin;

/// <summary>
/// 插件间消息总线。支持点对点（pluginId 过滤）和广播两种模式。
/// 发布方无需关心订阅方数量，订阅方通过异步枚举消费消息。
/// </summary>
public interface IPluginMessageBus
{
    /// <summary>发布消息到所有匹配的订阅者（非阻塞，Channel 满时丢弃旧消息）。</summary>
    ValueTask PublishAsync(PluginMessage message, CancellationToken ct = default);

    /// <summary>创建订阅，可选按 pluginId 或自定义 filter 过滤消息。</summary>
    IPluginSubscription Subscribe(string pluginId, Func<PluginMessage, bool>? filter = null);
}

/// <summary>消息订阅句柄，通过 <see cref="Messages"/> 异步枚举消费。释放时自动注销订阅。</summary>
public interface IPluginSubscription : IAsyncDisposable
{
    IAsyncEnumerable<PluginMessage> Messages { get; }
}
