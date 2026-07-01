using WhiteBoard.Contracts.Services;

namespace WhiteBoard.Core.Algorithms;

/// <summary>
/// 压感计算工具：
/// <list type="bullet">
///   <item><see cref="Normalize"/> — 将数字板原始压感值归一化至 [0.05, 1.0]。</item>
///   <item><see cref="EstimateFromVelocity"/> — 对不支持压感的设备（触摸屏），由速度反向推算压感（快划=轻按）。</item>
///   <item><see cref="SmoothPressure"/> — 指数移动平均平滑压感序列，消除数字化噪声抖动。</item>
///   <item><see cref="PressureToWidthMultiplier"/> — 压感到线宽倍率的幂曲线映射。</item>
/// </list>
/// </summary>
public static class PressureCalculator
{
    private const float MinPressure = 0.05f;
    private const float MaxPressure = 1.00f;
    private const float VelocitySoftCap = 800f; // px/s where pressure reaches minimum

    /// <summary>
    /// Normalises a raw pressure value in [0, maxRaw] to [MinPressure, 1].
    /// Call this when the digitizer reports real pressure (e.g. Wacom, Surface).
    /// </summary>
    public static float Normalize(float rawPressure, float maxRaw = 1023f)
    {
        if (maxRaw <= 0f) return 0.5f;
        float normalized = rawPressure / maxRaw;
        return Math.Clamp(normalized, MinPressure, MaxPressure);
    }

    /// <summary>
    /// Estimates pressure from the speed of the stylus between two consecutive points.
    /// Faster strokes produce lighter pressure (lower value), following the artistic
    /// convention that quick gestures lift the pen.
    /// </summary>
    public static float EstimateFromVelocity(
        PressurePoint previous,
        PressurePoint current)
    {
        float dt = (current.TimestampMs - previous.TimestampMs) / 1000f; // s
        if (dt <= 0f) return 0.5f;

        float dx = current.X - previous.X;
        float dy = current.Y - previous.Y;
        float speed = MathF.Sqrt(dx * dx + dy * dy) / dt; // px/s

        // Inverse relationship: high speed → low pressure
        float t = Math.Clamp(speed / VelocitySoftCap, 0f, 1f);
        return MaxPressure + (MinPressure - MaxPressure) * t;
    }

    /// <summary>
    /// Smooths a sequence of pressure samples with an exponential moving average,
    /// reducing jitter from noisy digitizer inputs.
    /// </summary>
    public static IReadOnlyList<PressurePoint> SmoothPressure(
        IReadOnlyList<PressurePoint> points,
        float alpha = 0.3f)
    {
        if (points.Count < 2) return points;

        var result = new PressurePoint[points.Count];
        result[0] = points[0];

        for (int i = 1; i < points.Count; i++)
        {
            float smoothed = alpha * points[i].Pressure + (1f - alpha) * result[i - 1].Pressure;
            result[i] = points[i] with { Pressure = smoothed };
        }

        return result;
    }

    /// <summary>
    /// Maps pressure in [0,1] to a stroke width multiplier.
    /// Uses a power curve so light touches produce noticeably thinner lines.
    /// </summary>
    public static float PressureToWidthMultiplier(float pressure, float exponent = 0.6f)
        => MathF.Pow(Math.Clamp(pressure, MinPressure, MaxPressure), exponent);
}