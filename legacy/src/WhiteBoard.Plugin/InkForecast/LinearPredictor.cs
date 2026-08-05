using WhiteBoard.Contracts.Services;

namespace WhiteBoard.Plugin.InkForecast;

/// <summary>
/// 线性外推预测器：基于最近 2-3 个点的平均速度向量做线性预测。
/// 适用于笔画开始段（points≤3）和急转兜底场景。
/// </summary>
public sealed class LinearPredictor : IStrokePredictor
{
    private const float MinDtMs = 1f;
    private const float MinLookahead = 12f;
    private const float MaxLookahead = 32f;
    private const int Steps = 4;

    public IReadOnlyList<PredictedPoint> Predict(
        IReadOnlyList<PressurePoint> recent,
        float lookaheadMs = 16f,
        bool isStrokeBegin = false)
    {
        if (recent.Count < 2) return [];

        // Average velocity over last min(3, n) points
        int window = Math.Min(recent.Count, 3);
        var first = recent[recent.Count - window];
        var last = recent[recent.Count - 1];
        float dtMs = Math.Max(MinDtMs, last.TimestampMs - first.TimestampMs);
        float vx = (last.X - first.X) / dtMs;
        float vy = (last.Y - first.Y) / dtMs;

        // Adaptive lookahead: faster stroke → longer window (12–24 ms)
        float speed = MathF.Sqrt(vx * vx + vy * vy);
        float adaptive = Math.Clamp(12f + speed * 4f, MinLookahead, 24f);
        float tMs = Math.Clamp(Math.Min(lookaheadMs, adaptive), MinLookahead, MaxLookahead);

        // Sharp-turn detection: cosine of angle between last two intervals < 0.3
        if (!isStrokeBegin && recent.Count >= 3)
        {
            var p0 = recent[recent.Count - 3];
            var p1 = recent[recent.Count - 2];
            var p2 = recent[recent.Count - 1];
            float dt0 = Math.Max(MinDtMs, p1.TimestampMs - p0.TimestampMs);
            float dt1 = Math.Max(MinDtMs, p2.TimestampMs - p1.TimestampMs);
            float vx0 = (p1.X - p0.X) / dt0, vy0 = (p1.Y - p0.Y) / dt0;
            float vx1 = (p2.X - p1.X) / dt1, vy1 = (p2.Y - p1.Y) / dt1;
            float dot = vx0 * vx1 + vy0 * vy1;
            float mag = MathF.Sqrt((vx0 * vx0 + vy0 * vy0) * (vx1 * vx1 + vy1 * vy1));
            if (mag > 0.001f && dot / mag < 0.3f)
                return [];
        }

        // Generate Steps predicted points with linearly decreasing confidence
        var result = new PredictedPoint[Steps];
        for (int i = 0; i < Steps; i++)
        {
            float t = tMs * (i + 1) / Steps;
            float conf = 0.85f - 0.14f * i;  // 0.85, 0.71, 0.57, 0.43
            result[i] = new PredictedPoint(
                X: last.X + vx * t,
                Y: last.Y + vy * t,
                Pressure: last.Pressure,
                Confidence: conf);
        }
        return result;
    }
}