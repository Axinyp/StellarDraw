using WhiteBoard.Contracts.Services;

namespace WhiteBoard.Core.Domain.Objects;

/// <summary>
/// 手写笔画对象：存储压感点序列和笔刷样式。
/// <para>
/// 使用 <c>StrokeObject(points, style)</c> 构造时会自动计算 Bounds（含笔宽半径 padding）。<br/>
/// 反序列化路径使用完整构造函数，由 <see cref="Services.PageSerializer"/> 传入已保存的 Bounds 和 Transform。
/// </para>
/// </summary>
public sealed class StrokeObject : WhiteboardObject
{
    public IReadOnlyList<PressurePoint> Points { get; }
    public StrokeStyle                  Style  { get; }

    public StrokeObject(IReadOnlyList<PressurePoint> points, StrokeStyle style)
    {
        Points = points;
        Style  = style;
        RecalculateBounds();
    }

    public StrokeObject(Guid id, IReadOnlyList<PressurePoint> points, StrokeStyle style,
        float bx, float by, float bw, float bh, float[] transform)
        : base(id, bx, by, bw, bh, transform)
    {
        Points = points;
        Style  = style;
    }

    private void RecalculateBounds()
    {
        if (Points.Count == 0) return;
        float minX = Points[0].X, minY = Points[0].Y;
        float maxX = minX,        maxY = minY;
        foreach (var p in Points)
        {
            if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
        }
        float pad = Style.Thickness * 0.5f;
        SetBounds(minX - pad, minY - pad, maxX - minX + Style.Thickness, maxY - minY + Style.Thickness);
    }
}

/// <summary>笔刷样式：笔型、墨水类型、颜色（ARGB）、粗细、填充、抗锯齿。</summary>
public sealed record StrokeStyle(
    PenType PenType,
    InkType InkType,
    uint    ColorArgb,
    float   Thickness,
    bool    IsFilled    = false,
    bool    IsAntiAlias = true);

public enum PenType  : byte { Pen = 0, Highlighter = 1, Marker = 2, Eraser = 3 }
public enum InkType  : byte { Normal = 0, Calligraphy = 1, Airbrush = 2 }
