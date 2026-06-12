using System.Windows;
using System.Windows.Media;
using WhiteBoard.Core.Algorithms;
using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;

namespace WhiteBoard.UI.Rendering;

/// <summary>
/// 白板对象渲染器：将 <see cref="PageModel"/> 中的所有对象绘制到 WPF <see cref="DrawingContext"/>。
/// <para>
/// 分两类方法：<br/>
/// - <see cref="RenderPage"/> / <see cref="RenderObject"/>：绘制已提交层（平滑+几何路径）。<br/>
/// - <see cref="RenderWetStroke"/>：绘制湿墨迹层（折线，无平滑，追求低延迟）。
/// </para>
/// 调用方负责在 UI 线程内通过 <c>DrawingVisual.RenderOpen()</c> 的 using 块调用。
/// </summary>
public static class InkRenderer
{
    /// <summary>重绘页面所有已提交对象到 dc（应提交层 DrawingVisual 专用）。</summary>
    public static void RenderPage(DrawingContext dc, PageModel page, Size canvasSize)
    {
        foreach (var obj in page.GetAllObjects())
            RenderObject(dc, obj);
    }

    public static void RenderObject(DrawingContext dc, WhiteboardObject obj)
    {
        switch (obj)
        {
            case StrokeObject s:
                RenderStroke(dc, s);
                break;
            case TextObject t:
                RenderText(dc, t);
                break;
            case ImageObject img:
                RenderImagePlaceholder(dc, img);
                break;
            default:
                RenderGenericBounds(dc, obj);
                break;
        }
    }

    // ── Stroke ────────────────────────────────────────────────────────────────

    public static void RenderStroke(DrawingContext dc, StrokeObject s)
    {
        if (s.Points.Count < 2) return;

        var smoothed = BezierSmoothing.Smooth(s.Points, iterations: 2);
        var geometry = BuildStrokeGeometry(smoothed);

        var color = ArgbToColor(s.Style.ColorArgb);
        if (s.Style.PenType == PenType.Highlighter)
            color = Color.FromArgb(120, color.R, color.G, color.B);

        var pen = new Pen(new SolidColorBrush(color), s.Style.Thickness)
        {
            LineJoin  = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap   = PenLineCap.Round,
        };

        var guideline = new GuidelineSet();
        dc.PushGuidelineSet(guideline);
        dc.DrawGeometry(s.Style.IsFilled ? new SolidColorBrush(color) : null, pen, geometry);
        dc.Pop();
    }

    /// <summary>渲染湿墨迹（正在绘制的笔画，仅连折线，无平滑）。</summary>
    public static void RenderWetStroke(
        DrawingContext dc,
        IReadOnlyList<WhiteBoard.Contracts.Services.PressurePoint> points,
        uint colorArgb,
        float thickness)
    {
        if (points.Count < 2) return;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(points[0].X, points[0].Y), false, false);
            for (int i = 1; i < points.Count; i++)
                ctx.LineTo(new Point(points[i].X, points[i].Y), true, false);
        }
        geo.Freeze();

        var color = ArgbToColor(colorArgb);
        var pen   = new Pen(new SolidColorBrush(color), thickness)
        {
            LineJoin     = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap   = PenLineCap.Round,
        };
        dc.DrawGeometry(null, pen, geo);
    }

    // ── Text ──────────────────────────────────────────────────────────────────

    private static void RenderText(DrawingContext dc, TextObject t)
    {
        var ft = new FormattedText(
            t.Content,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(t.Style.FontFamily),
            t.Style.FontSize,
            new SolidColorBrush(ArgbToColor(t.Style.ColorArgb)),
            pixelsPerDip: 1.0);

        if (t.Style.Bold)   ft.SetFontWeight(FontWeights.Bold,   0, t.Content.Length);
        if (t.Style.Italic) ft.SetFontStyle(FontStyles.Italic, 0, t.Content.Length);

        dc.DrawText(ft, new Point(t.BoundsX, t.BoundsY));
    }

    // ── Placeholders for non-stroke types ────────────────────────────────────

    private static void RenderImagePlaceholder(DrawingContext dc, ImageObject img)
    {
        var rect = new Rect(img.BoundsX, img.BoundsY, img.BoundsWidth, img.BoundsHeight);
        dc.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(40, 100, 149, 237)),
            new Pen(Brushes.CornflowerBlue, 1),
            rect);
    }

    private static void RenderGenericBounds(DrawingContext dc, WhiteboardObject obj)
    {
        if (obj.BoundsWidth <= 0 || obj.BoundsHeight <= 0) return;
        var rect = new Rect(obj.BoundsX, obj.BoundsY, obj.BoundsWidth, obj.BoundsHeight);
        dc.DrawRectangle(null, new Pen(Brushes.Gray, 1) { DashStyle = DashStyles.Dash }, rect);
    }

    // ── Geometry helpers ──────────────────────────────────────────────────────

    private static Geometry BuildStrokeGeometry(
        IReadOnlyList<WhiteBoard.Contracts.Services.PressurePoint> pts)
    {
        var geo = new StreamGeometry();
        using var ctx = geo.Open();

        ctx.BeginFigure(new Point(pts[0].X, pts[0].Y), false, false);

        // Catmull-Rom–like cubic segments for smoothed points
        for (int i = 1; i < pts.Count - 2; i += 2)
        {
            ctx.BezierTo(
                new Point(pts[i].X,     pts[i].Y),
                new Point(pts[i+1].X,   pts[i+1].Y),
                new Point(pts[Math.Min(i+2, pts.Count-1)].X,
                           pts[Math.Min(i+2, pts.Count-1)].Y),
                true, false);
        }
        // Tail: remaining points as line segments
        for (int i = (pts.Count - 1) & ~1; i < pts.Count; i++)
            ctx.LineTo(new Point(pts[i].X, pts[i].Y), true, false);

        geo.Freeze();
        return geo;
    }

    private static Color ArgbToColor(uint argb)
        => Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte) argb);
}
