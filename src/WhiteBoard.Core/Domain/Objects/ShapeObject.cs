namespace WhiteBoard.Core.Domain.Objects;

/// <summary>几何形状对象，由 AI 工具（insert_shape）或未来形状工具插入。</summary>
public sealed class ShapeObject : WhiteboardObject
{
    public ShapeType Type { get; init; }
    public uint FillArgb { get; init; }
    public uint StrokeArgb { get; init; }
    public float StrokeWidth { get; init; }
    public bool IsFilled { get; init; }

    public ShapeObject(ShapeType type, uint fillArgb, uint strokeArgb, float strokeWidth, bool isFilled,
        float x, float y, float width, float height)
    {
        Type = type;
        FillArgb = fillArgb;
        StrokeArgb = strokeArgb;
        StrokeWidth = strokeWidth;
        IsFilled = isFilled;
        SetBounds(x, y, width, height);
    }

    public ShapeObject(Guid id, ShapeType type, uint fillArgb, uint strokeArgb, float strokeWidth, bool isFilled,
        float bx, float by, float bw, float bh, float[] transform)
        : base(id, bx, by, bw, bh, transform)
    {
        Type = type;
        FillArgb = fillArgb;
        StrokeArgb = strokeArgb;
        StrokeWidth = strokeWidth;
        IsFilled = isFilled;
    }
}

public enum ShapeType : byte
{
    Rectangle = 1,
    Circle = 2,
    Line = 3,
    Arrow = 4,
    Triangle = 5,
}

/// <summary>
/// 形状类型字符串解析（含常见别名容错）。统一 UI 工具栏与 AI 工具的解析逻辑。
/// 注意：Circle 渲染即按 Bounds 画椭圆，故 ellipse/oval 也映射到 Circle。
/// </summary>
public static class ShapeTypeParser
{
    /// <summary>供工具描述/错误信息展示的受支持取值。</summary>
    public const string Supported = "rectangle, circle, ellipse, triangle, line, arrow";

    /// <summary>解析形状类型；识别失败返回 false（调用方决定报错或回退）。</summary>
    public static bool TryParse(string? value, out ShapeType type)
    {
        type = ShapeType.Rectangle;
        if (string.IsNullOrWhiteSpace(value)) return false;

        switch (value.Trim().ToLowerInvariant())
        {
            case "rectangle" or "rect" or "square":
                type = ShapeType.Rectangle; return true;
            case "circle" or "ellipse" or "oval":
                type = ShapeType.Circle; return true;   // Circle 按 Bounds 画椭圆，宽≠高即椭圆
            case "line":
                type = ShapeType.Line; return true;
            case "arrow":
                type = ShapeType.Arrow; return true;
            case "triangle":
                type = ShapeType.Triangle; return true;
            default:
                return false;
        }
    }
}