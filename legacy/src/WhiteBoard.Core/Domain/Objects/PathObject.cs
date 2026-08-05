namespace WhiteBoard.Core.Domain.Objects;

using WhiteBoard.Core.Algorithms;

/// <summary>
/// 通用矢量路径对象。使用 SVG path string（如 "M 100 100 L 200 300 Z"）描述任意几何轮廓。
/// 覆盖多边形、星形、贝塞尔曲线、任意轮廓——永远不需要新增 ObjectKind。
/// WPF 渲染层通过 <c>Geometry.Parse(PathData)</c> 直接解析。
/// </summary>
public sealed class PathObject : WhiteboardObject
{
    /// <summary>SVG path string，例如 "M 0 0 L 100 0 L 50 86.6 Z"（正三角形）。</summary>
    public string PathData { get; init; }

    public uint FillArgb { get; init; }
    public uint StrokeArgb { get; init; }
    public float StrokeWidth { get; init; }
    public bool IsFilled { get; init; }

    public PathObject(
        string pathData,
        uint fillArgb,
        uint strokeArgb,
        float strokeWidth,
        bool isFilled)
    {
        PathData = pathData;
        FillArgb = fillArgb;
        StrokeArgb = strokeArgb;
        StrokeWidth = strokeWidth;
        IsFilled = isFilled;

        var bounds = ComputeBounds(pathData);
        SetBounds(bounds.x, bounds.y, bounds.w, bounds.h);
    }

    public PathObject(
        Guid id,
        string pathData,
        uint fillArgb,
        uint strokeArgb,
        float strokeWidth,
        bool isFilled,
        float bx, float by, float bw, float bh,
        float[] transform)
        : base(id, bx, by, bw, bh, transform)
    {
        PathData = pathData;
        FillArgb = fillArgb;
        StrokeArgb = strokeArgb;
        StrokeWidth = strokeWidth;
        IsFilled = isFilled;
    }

    /// <summary>
    /// 计算 path 的纯几何包围盒（不含描边外扩，与 ShapeObject 语义一致）。
    /// 委托 <see cref="SvgPathBounds"/> 正确解析 SVG（绝对/相对命令、贝塞尔极值、弧采样），
    /// 使 Bounds 与渲染层 <c>Geometry.Parse</c> 的实际几何一致，避免选中框相对图形偏移。
    /// </summary>
    private static (float x, float y, float w, float h) ComputeBounds(string pathData)
        => SvgPathBounds.Compute(pathData);
}