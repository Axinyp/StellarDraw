using WhiteBoard.Contracts.Services;

namespace WhiteBoard.Core.Algorithms;

/// <summary>
/// 笔迹平滑工具：
/// <list type="bullet">
///   <item><see cref="Smooth"/> — Chaikin 角切割算法，将稀疏触控点收敛为二次 B-样条，视觉丝滑且不超调。</item>
///   <item><see cref="Simplify"/> — Ramer–Douglas–Peucker 降采样，在平滑前去除近似共线的冗余点。</item>
/// </list>
/// 建议调用顺序：Simplify → Smooth，减少 Chaikin 迭代放大噪声的风险。
/// </summary>
public static class BezierSmoothing
{
    /// <summary>
    /// Smooths a stroke by applying Chaikin's algorithm <paramref name="iterations"/> times.
    /// Points are retained at the endpoints to preserve stroke extent.
    /// Pressure is interpolated linearly alongside position.
    /// </summary>
    public static IReadOnlyList<PressurePoint> Smooth(
        IReadOnlyList<PressurePoint> points,
        int   iterations = 2,
        float ratio      = 0.25f)
    {
        if (points.Count < 3) return points;

        var src = points;
        for (int iter = 0; iter < iterations; iter++)
            src = ChaikinPass(src, ratio);

        return src;
    }

    private static IReadOnlyList<PressurePoint> ChaikinPass(
        IReadOnlyList<PressurePoint> src,
        float ratio)
    {
        var dst = new List<PressurePoint>(src.Count * 2) { src[0] };

        for (int i = 0; i < src.Count - 1; i++)
        {
            var a = src[i];
            var b = src[i + 1];

            float t0 = 1f - ratio;
            float t1 = ratio;

            dst.Add(new PressurePoint(
                a.X * t0 + b.X * t1,
                a.Y * t0 + b.Y * t1,
                a.Pressure * t0 + b.Pressure * t1,
                a.TimestampMs));

            dst.Add(new PressurePoint(
                a.X * t1 + b.X * t0,
                a.Y * t1 + b.Y * t0,
                a.Pressure * t1 + b.Pressure * t0,
                b.TimestampMs));
        }

        dst.Add(src[^1]);
        return dst;
    }

    /// <summary>
    /// Reduces redundant collinear points using the Ramer–Douglas–Peucker algorithm.
    /// Call before smoothing to avoid amplifying noise from near-duplicate samples.
    /// </summary>
    public static IReadOnlyList<PressurePoint> Simplify(
        IReadOnlyList<PressurePoint> points,
        float epsilon = 0.5f)
    {
        if (points.Count < 3) return points;

        var result = new List<PressurePoint>();
        RdpReduce(points, 0, points.Count - 1, epsilon * epsilon, result);
        result.Add(points[^1]);
        return result;
    }

    private static void RdpReduce(
        IReadOnlyList<PressurePoint> pts,
        int first, int last,
        float epsilonSq,
        List<PressurePoint> result)
    {
        result.Add(pts[first]);
        if (last - first < 2) return;

        float maxDistSq = 0f;
        int   splitIdx  = first;

        var p0 = pts[first];
        var p1 = pts[last];
        float dx = p1.X - p0.X;
        float dy = p1.Y - p0.Y;
        float lenSq = dx * dx + dy * dy;

        for (int i = first + 1; i < last; i++)
        {
            float d = lenSq < float.Epsilon
                ? DistSq(pts[i], p0)
                : PerpendicularDistSq(pts[i], p0, dx, dy, lenSq);

            if (d > maxDistSq) { maxDistSq = d; splitIdx = i; }
        }

        if (maxDistSq <= epsilonSq) return;

        RdpReduce(pts, first,    splitIdx, epsilonSq, result);
        RdpReduce(pts, splitIdx, last,     epsilonSq, result);
    }

    private static float DistSq(PressurePoint p, PressurePoint q)
    {
        float dx = p.X - q.X, dy = p.Y - q.Y;
        return dx * dx + dy * dy;
    }

    private static float PerpendicularDistSq(
        PressurePoint p,
        PressurePoint lineStart,
        float dx, float dy, float lenSq)
    {
        float t = ((p.X - lineStart.X) * dx + (p.Y - lineStart.Y) * dy) / lenSq;
        float px = lineStart.X + t * dx - p.X;
        float py = lineStart.Y + t * dy - p.Y;
        return px * px + py * py;
    }
}
