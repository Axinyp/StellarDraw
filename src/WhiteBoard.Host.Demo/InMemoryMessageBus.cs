using System.Runtime.CompilerServices;
using System.Threading.Channels;
using WhiteBoard.Contracts.Models;
using WhiteBoard.Contracts.Plugin;

namespace WhiteBoard.Host.Demo;

/// <summary>
/// 基于 Channel 的内存消息总线。
/// 每个订阅者独享一个有界 Channel；Channel 满时丢弃并计数（不阻塞 PublishAsync）。
/// </summary>
public sealed class InMemoryMessageBus : IPluginMessageBus
{
    private readonly object _lock = new();
    private readonly List<Subscription> _subscriptions = [];
    private long _sequence;

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

    public IPluginSubscription Subscribe(string pluginId, Func<PluginMessage, bool>? filter = null)
    {
        var channel = Channel.CreateBounded<PluginMessage>(new BoundedChannelOptions(512)
        {
            FullMode       = BoundedChannelFullMode.DropOldest,
            SingleReader   = true,
            SingleWriter   = false,
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

    public long NextSequence() => Interlocked.Increment(ref _sequence);

    // ── Inner types ──────────────────────────────────────────────────────────

    internal sealed class Subscription : IPluginSubscription
    {
        private readonly InMemoryMessageBus _bus;
        private long _dropped;

        internal readonly ChannelWriter<PluginMessage> Writer;
        private  readonly ChannelReader<PluginMessage> _reader;

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
            Filter   = filter;
            Writer   = writer;
            _reader  = reader;
            _bus     = bus;
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
