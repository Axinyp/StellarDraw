namespace WhiteBoard.Core.Domain;

/// <summary>
/// 白板坐标系的数值边界校验，确保空间索引和渲染层不会接收不可表示的包围盒。
/// </summary>
public static class WhiteboardGeometry
{
    /// <summary>白板坐标及包围盒边界允许的最大绝对值。</summary>
    public const float MaxAbsoluteCoordinate = 1_000_000f;

    /// <summary>验证一个可安全进入空间索引的轴对齐包围盒。</summary>
    public static bool HasValidBounds(float x, float y, float width, float height)
    {
        if (!IsFiniteCoordinate(x) || !IsFiniteCoordinate(y) ||
            !IsPositiveExtent(width) || !IsPositiveExtent(height))
            return false;

        var right = (double)x + width;
        var bottom = (double)y + height;
        return IsFiniteCoordinate(right) && IsFiniteCoordinate(bottom);
    }

    private static bool IsFiniteCoordinate(double value) =>
        double.IsFinite(value) && Math.Abs(value) <= MaxAbsoluteCoordinate;

    private static bool IsPositiveExtent(float value) =>
        float.IsFinite(value) && value > 0 && value <= MaxAbsoluteCoordinate;
}
