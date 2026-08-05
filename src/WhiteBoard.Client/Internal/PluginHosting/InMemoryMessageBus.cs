using System.Threading.Channels;
using WhiteBoard.Contracts.Plugin;

namespace WhiteBoard.Client.Internal.PluginHosting;

/// <summary>
/// 基于 Channel 的内存消息总线。
/// 首轮真实业务回接阶段只需要“插件初始化时能拿到一个不会阻塞的消息总线”，
/// 因此继续复用 Demo 已验证过的轻量实现，而不在 Client 中同时引入跨进程总线或远程消息分发。
/// </summary>
internal sealed class InMemoryMessageBus : IPluginMessageBus
{
    private readonly object _lock = new();
    private readonly List<Subscription> _subscriptions = [];
    private long _sequence;

    /// <summary>
    /// 发布消息到所有匹配的订阅者。
    /// 这里刻意采用“非阻塞写入 + 队列满时丢弃最旧消息”的策略，
    /// 因为首轮目标是保证宿主不会因为插件消息堆积而卡住主链路。
    /// 若未来真的需要可靠消息投递，再单独升级该总线模型。
    /// </summary>
    public ValueTask PublishAsync(PluginMessage message, CancellationToken ct = default)
    {
        List<Subscription> snapshot;
        lock (_lock)
            snapshot = [.. _subscriptions];

        foreach (var sub in snapshot)
        {
            if (sub.Filter is not null && !sub.Filter(message))
                continue;

            if (!sub.Writer.TryWrite(message))
                sub.IncrementDropped();
        }

        return default;
    }

    /// <summary>
    /// 创建一个新的订阅句柄。
    /// 每个订阅者独享一个有界 Channel，避免单个慢消费者拖垮全部插件通信。
    /// </summary>
    public IPluginSubscription Subscribe(string pluginId, Func<PluginMessage, bool>? filter = null)
    {
        var channel = Channel.CreateBounded<PluginMessage>(new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        var sub = new Subscription(pluginId, filter, channel.Writer, channel.Reader, this);

        lock (_lock)
            _subscriptions.Add(sub);

        return sub;
    }

    internal void Unsubscribe(Subscription sub)
    {
        lock (_lock)
            _subscriptions.Remove(sub);
        sub.Writer.TryComplete();
    }

    internal long NextSequence() => Interlocked.Increment(ref _sequence);

    internal sealed class Subscription : IPluginSubscription
    {
        private readonly InMemoryMessageBus _bus;
        private long _dropped;

        internal readonly ChannelWriter<PluginMessage> Writer;
        private readonly ChannelReader<PluginMessage> _reader;

        internal Func<PluginMessage, bool>? Filter { get; }

        public string PluginId { get; }

        public IAsyncEnumerable<PluginMessage> Messages => _reader.ReadAllAsync();

        internal Subscription(
            string pluginId,
            Func<PluginMessage, bool>? filter,
            ChannelWriter<PluginMessage> writer,
            ChannelReader<PluginMessage> reader,
            InMemoryMessageBus bus)
        {
            PluginId = pluginId;
            Filter = filter;
            Writer = writer;
            _reader = reader;
            _bus = bus;
        }

        internal void IncrementDropped() => Interlocked.Increment(ref _dropped);

        public long DroppedCount => Volatile.Read(ref _dropped);

        public ValueTask DisposeAsync()
        {
            _bus.Unsubscribe(this);
            return default;
        }
    }
}
