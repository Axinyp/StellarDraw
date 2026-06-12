using WhiteBoard.Contracts.Services;

namespace WhiteBoard.Core.Algorithms;

/// <summary>
/// Point-in-polygon and lasso selection utilities.
/// All coordinates are in document space (logical units).
/// </summary>
public static class LassoHitTest
{
    /// <summary>
    /// Returns true if <paramref name="point"/> is inside the polygon formed by
    /// <paramref name="lasso"/> using the even-odd (ray casting) rule.
    /// </summary>
    public static bool ContainsPoint(IReadOnlyList<(float X, float Y)> lasso, float x, float y)
    {
        int count = lasso.Count;
        if (count < 3) return false;

        bool inside = false;
        int j = count - 1;
        for (int i = 0; i < count; j = i++)
        {
            float xi = lasso[i].X, yi = lasso[i].Y;
            float xj = lasso[j].X, yj = lasso[j].Y;

            if ((yi > y) != (yj > y) &&
                x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>
    /// Returns true if the axis-aligned bounding box defined by the given coordinates
    /// overlaps the lasso polygon. Uses a conservative AABB-vs-lasso test:
    /// true if any corner of the box is inside the lasso, or any lasso vertex
    /// is inside the box, or any lasso edge crosses the box.
    /// </summary>
    public static bool IntersectsAabb(
        IReadOnlyList<(float X, float Y)> lasso,
        float bx, float by, float bw, float bh)
    {
        float right  = bx + bw;
        float bottom = by + bh;

        // Any corner of AABB inside lasso?
        if (ContainsPoint(lasso, bx,    by)   ||
            ContainsPoint(lasso, right, by)   ||
            ContainsPoint(lasso, right, bottom) ||
            ContainsPoint(lasso, bx,    bottom))
            return true;

        // Any lasso vertex inside AABB?
        foreach (var (lx, ly) in lasso)
        {
            if (lx >= bx && lx <= right && ly >= by && ly <= bottom)
                return true;
        }

        // Any lasso edge crosses AABB border?
        int count = lasso.Count;
        int j = count - 1;
        for (int i = 0; i < count; j = i++)
        {
            if (SegmentIntersectsAabb(
                lasso[j].X, lasso[j].Y,
                lasso[i].X, lasso[i].Y,
                bx, by, right, bottom))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Converts a stroke point list into a lasso polygon by pairing the forward
    /// path with a 1-pixel-wide reverse path, forming a closed selection band.
    /// </summary>
    public static IReadOnlyList<(float X, float Y)> StrokeToLasso(
        IReadOnlyList<PressurePoint> points,
        float halfWidth = 4f)
    {
        if (points.Count < 2) return [];

        var forward  = new List<(float, float)>(points.Count);
        var backward = new List<(float, float)>(points.Count);

        for (int i = 0; i < points.Count; i++)
        {
            float nx, ny;
            if (i == 0)
            {
                nx = points[1].Y - points[0].Y;
                ny = points[0].X - points[1].X;
            }
            else if (i == points.Count - 1)
            {
                nx = points[i].Y - points[i - 1].Y;
                ny = points[i - 1].X - points[i].X;
            }
            else
            {
                nx = points[i + 1].Y - points[i - 1].Y;
                ny = points[i - 1].X - points[i + 1].X;
            }

            float len = MathF.Sqrt(nx * nx + ny * ny);
            if (len < float.Epsilon) { nx = 0f; ny = halfWidth; }
            else { nx = nx / len * halfWidth; ny = ny / len * halfWidth; }

            forward.Add((points[i].X + nx, points[i].Y + ny));
            backward.Add((points[i].X - nx, points[i].Y - ny));
        }

        backward.Reverse();
        forward.AddRange(backward);
        return forward;
    }

    // Cohen-Sutherland line-clip: returns true if segment (x0,y0)→(x1,y1) intersects [left,right]×[top,bottom]
    private static bool SegmentIntersectsAabb(
        float x0, float y0, float x1, float y1,
        float left, float top, float right, float bottom)
    {
        const int L = 1, R = 2, B = 4, T = 8;

        int Code(float x, float y)
        {
            int c = 0;
            if (x < left)   c |= L;
            if (x > right)  c |= R;
            if (y < top)    c |= B;
            if (y > bottom) c |= T;
            return c;
        }

        int c0 = Code(x0, y0), c1 = Code(x1, y1);

        while (true)
        {
            if ((c0 | c1) == 0) return true;   // both inside
            if ((c0 & c1) != 0) return false;  // both on same outside

            int code = c0 != 0 ? c0 : c1;
            float x, y;

            if ((code & T) != 0)      { x = x0 + (x1 - x0) * (bottom - y0) / (y1 - y0); y = bottom; }
            else if ((code & B) != 0) { x = x0 + (x1 - x0) * (top    - y0) / (y1 - y0); y = top; }
            else if ((code & R) != 0) { y = y0 + (y1 - y0) * (right  - x0) / (x1 - x0); x = right; }
            else                       { y = y0 + (y1 - y0) * (left   - x0) / (x1 - x0); x = left; }

            if (code == c0) { x0 = x; y0 = y; c0 = Code(x0, y0); }
            else             { x1 = x; y1 = y; c1 = Code(x1, y1); }
        }
    }
}
