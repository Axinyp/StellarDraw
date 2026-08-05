using System.Threading.Channels;
using WhiteBoard.Contracts.Plugin;

namespace StellarDraw.Axinyp.Spike;

/// <summary>
/// consumer-private 的有界内存消息总线。
/// 仅满足 legacy plugin 初始化与离线运行，未形成 Axinyp 消息总线抽象。
/// </summary>
internal sealed class LegacyPluginMessageBus : IPluginMessageBus
{
    private readonly object _gate = new();
    private readonly List<Subscription> _subscriptions = [];

    /// <inheritdoc />
    public ValueTask PublishAsync(PluginMessage message, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        List<Subscription> subscriptions;
        lock (_gate)
            subscriptions = [.. _subscriptions];

        foreach (var subscription in subscriptions)
        {
            if (subscription.Accepts(message))
                subscription.Writer.TryWrite(message);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public IPluginSubscription Subscribe(string pluginId, Func<PluginMessage, bool>? filter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        var channel = Channel.CreateBounded<PluginMessage>(new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        var subscription = new Subscription(pluginId, filter, channel.Reader, channel.Writer, this);

        lock (_gate)
            _subscriptions.Add(subscription);

        return subscription;
    }

    private void Unsubscribe(Subscription subscription)
    {
        lock (_gate)
            _subscriptions.Remove(subscription);
        subscription.Writer.TryComplete();
    }

    private sealed class Subscription(
        string pluginId,
        Func<PluginMessage, bool>? filter,
        ChannelReader<PluginMessage> reader,
        ChannelWriter<PluginMessage> writer,
        LegacyPluginMessageBus owner) : IPluginSubscription
    {
        public ChannelWriter<PluginMessage> Writer { get; } = writer;

        public IAsyncEnumerable<PluginMessage> Messages => reader.ReadAllAsync();

        public bool Accepts(PluginMessage message) =>
            (string.IsNullOrWhiteSpace(message.TargetPluginId) ||
             string.Equals(message.TargetPluginId, pluginId, StringComparison.Ordinal)) &&
            (filter is null || filter(message));

        public ValueTask DisposeAsync()
        {
            owner.Unsubscribe(this);
            return ValueTask.CompletedTask;
        }
    }
}
