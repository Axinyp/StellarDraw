using System.Windows;
using System.Windows.Media;
using WhiteBoard.Contracts.Services;

namespace WhiteBoard.UI.Rendering;

/// <summary>
/// 将预测笔迹渲染为由近及远渐隐的平滑曲线（Catmull-Rom 贝塞尔）。
/// 近端不透明度约 50%，远端衰减至约 5%，消失时视觉冲击极小。
/// </summary>
public static class PredictedInkRenderer
{
    public static void RenderPredictedStroke(
        DrawingContext                dc,
        IReadOnlyList<PredictedPoint> predicted,
        IReadOnlyList<PressurePoint>  confirmed,
        uint                          colorArgb,
        float                         thickness)
    {
        if (predicted.Count == 0 || confirmed.Count < 2) return;

        // 全序列：confirmed[-2], confirmed[-1], pred[0..n-1]
        int total = 2 + predicted.Count;
        var xs = new float[total];
        var ys = new float[total];
        xs[0] = confirmed[confirmed.Count - 2].X;
        ys[0] = confirmed[confirmed.Count - 2].Y;
        xs[1] = confirmed[confirmed.Count - 1].X;
        ys[1] = confirmed[confirmed.Count - 1].Y;
        for (int i = 0; i < predicted.Count; i++)
        {
            xs[i + 2] = predicted[i].X;
            ys[i + 2] = predicted[i].Y;
        }

        var base_ = ArgbToColor(colorArgb);

        // 每段单独渲染，不透明度由近到远线性衰减
        // 段 0（confirmed[-1]→pred[0]）：50%  末段：5%
        int segCount = total - 2;   // 从 xs[1] 到 xs[total-1]，共 predicted.Count 段
        for (int seg = 0; seg < segCount; seg++)
        {
            // i = seg+1：当前段起点在 all[] 中的索引
            int i = seg + 1;
            float t = segCount > 1 ? (float)seg / (segCount - 1) : 0f;
            float opacity = (0.50f + (0.05f - 0.50f) * t) * (base_.A / 255f);

            float p0x = xs[i - 1],   p0y = ys[i - 1];
            float p1x = xs[i],       p1y = ys[i];
            float p2x = xs[i + 1],   p2y = ys[i + 1];
            float p3x = i + 2 < total ? xs[i + 2] : p2x + (p2x - p1x);
            float p3y = i + 2 < total ? ys[i + 2] : p2y + (p2y - p1y);

            float cp1x = p1x + (p2x - p0x) / 6f;
            float cp1y = p1y + (p2y - p0y) / 6f;
            float cp2x = p2x - (p3x - p1x) / 6f;
            float cp2y = p2y - (p3y - p1y) / 6f;

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(p1x, p1y), false, false);
                ctx.BezierTo(
                    new Point(cp1x, cp1y),
                    new Point(cp2x, cp2y),
                    new Point(p2x,  p2y),
                    isStroked: true, isSmoothJoin: false);
            }
            geo.Freeze();

            var color = Color.FromArgb((byte)(255 * opacity), base_.R, base_.G, base_.B);
            var pen   = new Pen(new SolidColorBrush(color), thickness)
            {
                LineJoin     = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap   = PenLineCap.Round,
            };
            dc.DrawGeometry(null, pen, geo);
        }
    }

    private static Color ArgbToColor(uint argb)
        => Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte) argb);
}
