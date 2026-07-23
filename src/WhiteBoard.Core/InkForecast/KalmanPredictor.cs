using WhiteBoard.Contracts.Services;

namespace WhiteBoard.Core.InkForecast;

/// <summary>
/// EMA-Kalman 混合预测器：EMA 平滑速度向量后做线性外推。
/// "曲线平滑"由渲染层的 Catmull-Rom 贝塞尔负责，预测器只提供稳定的方向。
/// 笔画开始（≤3 点）或急转时退化为 LinearPredictor。
/// </summary>
public sealed class KalmanPredictor : IStrokePredictor
{
    private const float MinDtMs = 1f;
    private const float AlphaVel = 0.35f;  // 速度 EMA（越小越平滑，越大越灵敏）
    private const float MinLookahead = 12f;
    private const float MaxLookahead = 32f;
    private const int Steps = 6;
    private const int MinPoints = 4;

    private readonly LinearPredictor _linear = new();

    public IReadOnlyList<PredictedPoint> Predict(
        IReadOnlyList<PressurePoint> recent,
        float lookaheadMs = 16f,
        bool isStrokeBegin = false)
    {
        if (isStrokeBegin || recent.Count < MinPoints)
            return _linear.Predict(recent, lookaheadMs, isStrokeBegin);

        // ── EMA 速度（最近 6 个间隔） ────────────────────────────────────────
        int velWindow = Math.Min(recent.Count - 1, 6);
        int velStart = recent.Count - 1 - velWindow;
        float vx = 0f, vy = 0f;
        bool first = true;

        for (int i = velStart; i < recent.Count - 1; i++)
        {
            var a = recent[i];
            var b = recent[i + 1];
            float dt = Math.Max(MinDtMs, b.TimestampMs - a.TimestampMs);
            float rx = (b.X - a.X) / dt;
            float ry = (b.Y - a.Y) / dt;
            if (first) { vx = rx; vy = ry; first = false; }
            else
            {
                vx = AlphaVel * rx + (1 - AlphaVel) * vx;
                vy = AlphaVel * ry + (1 - AlphaVel) * vy;
            }
        }

        float speed = MathF.Sqrt(vx * vx + vy * vy);
        var last = recent[recent.Count - 1];

        // 突然减速 → 不预测
        if (recent.Count >= 2)
        {
            var prev = recent[recent.Count - 2];
            float dt1 = Math.Max(MinDtMs, last.TimestampMs - prev.TimestampMs);
            float rvx1 = (last.X - prev.X) / dt1;
            float rvy1 = (last.Y - prev.Y) / dt1;
            float lastSpd = MathF.Sqrt(rvx1 * rvx1 + rvy1 * rvy1);
            if (lastSpd < speed * 0.15f) return [];

            // 急转 → 不预测
            float dot = vx * rvx1 + vy * rvy1;
            float mag = speed * lastSpd;
            if (mag > 0.001f && dot / mag < 0.3f) return [];
        }

        // ── 起始位置去噪（加权均值） ──────────────────────────────────────────
        float baseX, baseY;
        if (recent.Count >= 3)
        {
            baseX = last.X * 0.6f + recent[recent.Count - 2].X * 0.28f + recent[recent.Count - 3].X * 0.12f;
            baseY = last.Y * 0.6f + recent[recent.Count - 2].Y * 0.28f + recent[recent.Count - 3].Y * 0.12f;
        }
        else { baseX = last.X; baseY = last.Y; }

        // ── 自适应前向时间（含减速感知） ─────────────────────────────────────
        float adaptive = Math.Clamp(12f + speed * 3f, MinLookahead, 24f);
        float tTotal = Math.Clamp(Math.Min(lookaheadMs, adaptive), MinLookahead, MaxLookahead);

        // 减速感知：最新瞬时速度 < EMA 速度的 70% 时，按比例缩短前向时间
        // 避免用户即将停笔时预测段仍然大幅超前
        if (recent.Count >= 2 && speed > 0.001f)
        {
            var prev1 = recent[recent.Count - 2];
            float dt1 = Math.Max(MinDtMs, last.TimestampMs - prev1.TimestampMs);
            float spd1 = MathF.Sqrt(MathF.Pow((last.X - prev1.X) / dt1, 2) +
                                         MathF.Pow((last.Y - prev1.Y) / dt1, 2));
            float ratio = spd1 / speed;          // < 1 表示正在减速
            if (ratio < 0.70f)
                tTotal *= Math.Clamp(ratio / 0.70f, 0.25f, 1f);
        }

        // ── 线性外推（方向稳定，曲线感由渲染层的 Catmull-Rom 提供） ───────────
        var result = new PredictedPoint[Steps];
        for (int i = 0; i < Steps; i++)
        {
            float t = tTotal * (i + 1) / Steps;
            float conf = 0.80f - 0.10f * i;
            result[i] = new PredictedPoint(
                X: baseX + vx * t,
                Y: baseY + vy * t,
                Pressure: last.Pressure,
                Confidence: conf);
        }
        return result;
    }
}