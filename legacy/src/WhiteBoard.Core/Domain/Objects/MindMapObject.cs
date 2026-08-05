using System.Text.Json;

namespace WhiteBoard.Core.Domain.Objects;

/// <summary>思维导图对象：以 <see cref="MindMapData"/> 树形结构存储节点，JSON 序列化后存入 PageSerializer 的 Payload 字段。</summary>
public sealed class MindMapObject : WhiteboardObject
{
    public MindMapData Data { get; set; }

    public MindMapObject(MindMapData data, float x, float y, float width, float height)
    {
        Data = data;
        SetBounds(x, y, width, height);
    }

    public MindMapObject(Guid id, MindMapData data,
        float bx, float by, float bw, float bh, float[] transform)
        : base(id, bx, by, bw, bh, transform)
    {
        Data = data;
    }
}

public sealed class MindMapData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public string Color { get; set; } = "#4A90D9";
    public List<MindMapData> Children { get; set; } = [];

    public string ToJson() => JsonSerializer.Serialize(this);

    public static MindMapData FromJson(string json) =>
        JsonSerializer.Deserialize<MindMapData>(json) ?? new MindMapData();
}