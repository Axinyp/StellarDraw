using System.Text.Json;

namespace WhiteBoard.Contracts.Plugin;

public sealed record PluginMessage(
    string MessageId,
    string Topic,
    string SourcePluginId,
    string? TargetPluginId,
    long Sequence,
    string SchemaVersion,
    string? CorrelationId,
    DateTimeOffset OccurredAtUtc,
    ReadOnlyMemory<byte> PayloadUtf8,
    string ContentType = "application/json"
)
{
    public static PluginMessage Create<T>(string topic, string source, T payload,
        string? correlationId = null, string? target = null)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload);
        return new PluginMessage(
            Guid.NewGuid().ToString("N"),
            topic,
            source,
            target,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            "1.0",
            correlationId,
            DateTimeOffset.UtcNow,
            json);
    }

    public T? DeserializePayload<T>() =>
        PayloadUtf8.IsEmpty ? default : JsonSerializer.Deserialize<T>(PayloadUtf8.Span);
}