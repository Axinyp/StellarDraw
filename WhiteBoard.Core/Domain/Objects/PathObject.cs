namespace WhiteBoard.Core.Domain.Objects;

/// <summary>
/// 通用矢量路径对象。使用 SVG path string（如 "M 100 100 L 200 300 Z"）描述任意几何轮廓。
/// 覆盖多边形、星形、贝塞尔曲线、任意轮廓——永远不需要新增 ObjectKind。
/// WPF 渲染层通过 <c>Geometry.Parse(PathData)</c> 直接解析。
/// </summary>
public sealed class PathObject : WhiteboardObject
{
    /// <summary>SVG path string，例如 "M 0 0 L 100 0 L 50 86.6 Z"（正三角形）。</summary>
    public string PathData    { get; init; }
    public uint   FillArgb    { get; init; }
    public uint   StrokeArgb  { get; init; }
    public float  StrokeWidth { get; init; }
    public bool   IsFilled    { get; init; }

    public PathObject(
        string pathData,
        uint   fillArgb,
        uint   strokeArgb,
        float  strokeWidth,
        bool   isFilled)
    {
        PathData    = pathData;
        FillArgb    = fillArgb;
        StrokeArgb  = strokeArgb;
        StrokeWidth = strokeWidth;
        IsFilled    = isFilled;

        var bounds = ComputeBounds(pathData);
        SetBounds(bounds.x, bounds.y, bounds.w, bounds.h);
    }

    public PathObject(
        Guid   id,
        string pathData,
        uint   fillArgb,
        uint   strokeArgb,
        float  strokeWidth,
        bool   isFilled,
        float bx, float by, float bw, float bh,
        float[] transform)
        : base(id, bx, by, bw, bh, transform)
    {
        PathData    = pathData;
        FillArgb    = fillArgb;
        StrokeArgb  = strokeArgb;
        StrokeWidth = strokeWidth;
        IsFilled    = isFilled;
    }

    private static (float x, float y, float w, float h) ComputeBounds(string pathData)
    {
        // 快速扫描 path string 中的数值对，估算包围盒。
        // 精确计算贝塞尔包围盒复杂度高且非必要（运行时 WPF Geometry.Bounds 已精确）。
        var nums   = new List<float>();
        var tokens = pathData.Split([' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var t in tokens)
            if (float.TryParse(t, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
                nums.Add(v);

        if (nums.Count < 2) return (0f, 0f, 0f, 0f);

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i + 1 < nums.Count; i += 2)
        {
            float x = nums[i], y = nums[i + 1];
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
        return (minX, minY, maxX - minX, maxY - minY);
    }
}
