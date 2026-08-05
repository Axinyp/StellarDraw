using System.Text.Json;
using System.Text.Json.Serialization;
using WhiteBoard.Contracts.Models;

namespace WhiteBoard.Contracts.Hub;

/// <summary>
/// 将 WhiteboardEvent 序列化为 HubEnvelope（用于 SignalR 发送），
/// 以及从 HubEnvelope 反序列化回 WhiteboardEvent（用于接收端解析）。
/// 内部使用类型鉴别符字符串实现多态，避免修改 Contracts 模型。
/// </summary>
public static class HubEnvelopeSerializer
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    // ── WhiteboardEvent → HubEnvelope ────────────────────────────────────────

    public static HubEnvelope? Wrap(WhiteboardEvent evt) => evt switch
    {
        StrokeAddedEvent e => new HubEnvelope("StrokeAdded", JsonSerializer.Serialize(e, Opts)),
        ObjectDeletedEvent e => new HubEnvelope("ObjectDeleted", JsonSerializer.Serialize(e, Opts)),
        ObjectTransformedEvent e => new HubEnvelope("ObjectTransformed", JsonSerializer.Serialize(e, Opts)),
        ObjectsTransformedEvent e => new HubEnvelope("ObjectsTransformed", JsonSerializer.Serialize(e, Opts)),
        ObjectAddedEvent e => new HubEnvelope("ObjectAdded", JsonSerializer.Serialize(e, Opts)),
        PageClearedEvent e => new HubEnvelope("PageCleared", JsonSerializer.Serialize(e, Opts)),
        PageAddedEvent e => new HubEnvelope("PageAdded", JsonSerializer.Serialize(e, Opts)),
        _ => null,
    };

    // ── HubEnvelope → WhiteboardEvent ────────────────────────────────────────

    public static WhiteboardEvent? Unwrap(HubEnvelope env)
    {
        try
        {
            return env.T switch
            {
                "StrokeAdded" => JsonSerializer.Deserialize<StrokeAddedEvent>(env.P, Opts),
                "ObjectDeleted" => JsonSerializer.Deserialize<ObjectDeletedEvent>(env.P, Opts),
                "ObjectTransformed" => JsonSerializer.Deserialize<ObjectTransformedEvent>(env.P, Opts),
                "ObjectsTransformed" => JsonSerializer.Deserialize<ObjectsTransformedEvent>(env.P, Opts),
                "ObjectAdded" => JsonSerializer.Deserialize<ObjectAddedEvent>(env.P, Opts),
                "PageCleared" => JsonSerializer.Deserialize<PageClearedEvent>(env.P, Opts),
                "PageAdded" => JsonSerializer.Deserialize<PageAddedEvent>(env.P, Opts),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── Envelope-level serialization ──────────────────────────────────────────

    public static string SerializeEnvelope(HubEnvelope env)
        => JsonSerializer.Serialize(env, Opts);

    public static HubEnvelope? DeserializeEnvelope(string json)
    {
        try { return JsonSerializer.Deserialize<HubEnvelope>(json, Opts); }
        catch { return null; }
    }
}