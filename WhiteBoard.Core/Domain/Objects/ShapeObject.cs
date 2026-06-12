namespace WhiteBoard.Core.Domain.Objects;

/// <summary>几何形状对象，由 AI 工具（insert_shape）或未来形状工具插入。</summary>
public sealed class ShapeObject : WhiteboardObject
{
    public ShapeType Type        { get; init; }
    public uint      FillArgb    { get; init; }
    public uint      StrokeArgb  { get; init; }
    public float     StrokeWidth { get; init; }
    public bool      IsFilled    { get; init; }

    public ShapeObject(ShapeType type, uint fillArgb, uint strokeArgb, float strokeWidth, bool isFilled,
        float x, float y, float width, float height)
    {
        Type        = type;
        FillArgb    = fillArgb;
        StrokeArgb  = strokeArgb;
        StrokeWidth = strokeWidth;
        IsFilled    = isFilled;
        SetBounds(x, y, width, height);
    }

    public ShapeObject(Guid id, ShapeType type, uint fillArgb, uint strokeArgb, float strokeWidth, bool isFilled,
        float bx, float by, float bw, float bh, float[] transform)
        : base(id, bx, by, bw, bh, transform)
    {
        Type        = type;
        FillArgb    = fillArgb;
        StrokeArgb  = strokeArgb;
        StrokeWidth = strokeWidth;
        IsFilled    = isFilled;
    }
}

public enum ShapeType : byte
{
    Rectangle = 1,
    Circle    = 2,
    Line      = 3,
    Arrow     = 4,
    Triangle  = 5,
}
