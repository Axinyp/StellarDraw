namespace WhiteBoard.Core.Domain.Objects;

/// <summary>文本对象：存储富文本内容与字体样式，由 <see cref="UI.Rendering.InkRenderer"/> 渲染为 FormattedText。</summary>
public sealed class TextObject : WhiteboardObject
{
    public string    Content { get; set; }
    public TextStyle Style   { get; set; }

    public TextObject(string content, TextStyle style, float x, float y, float width, float height)
    {
        Content = content;
        Style   = style;
        SetBounds(x, y, width, height);
    }

    public TextObject(Guid id, string content, TextStyle style,
        float bx, float by, float bw, float bh, float[] transform)
        : base(id, bx, by, bw, bh, transform)
    {
        Content = content;
        Style   = style;
    }
}

public sealed record TextStyle(
    string FontFamily,
    float  FontSize,
    bool   Bold,
    bool   Italic,
    uint   ColorArgb);
