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
            case ShapeObject sh:
                RenderShape(dc, sh);
                break;
            case PathObject path:
                RenderPath(dc, path);
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

    // ── Path (SVG path string → WPF Geometry) ────────────────────────────────

    private static void RenderPath(DrawingContext dc, PathObject path)
    {
        if (string.IsNullOrWhiteSpace(path.PathData)) return;

        Geometry? geo;
        try   { geo = Geometry.Parse(path.PathData); }
        catch { return; }

        geo.Freeze();

        var fillColor   = ArgbToColor(path.FillArgb);
        var strokeColor = ArgbToColor(path.StrokeArgb);
        var fill   = path.IsFilled ? new SolidColorBrush(fillColor) : null;
        var stroke = new Pen(new SolidColorBrush(strokeColor), path.StrokeWidth)
        {
            LineJoin     = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap   = PenLineCap.Round,
        };

        dc.DrawGeometry(fill, stroke, geo);
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

    // ── Shape ─────────────────────────────────────────────────────────────────

    private static void RenderShape(DrawingContext dc, ShapeObject sh)
    {
        var fill   = sh.IsFilled ? new SolidColorBrush(ArgbToColor(sh.FillArgb)) : null;
        var stroke = new Pen(new SolidColorBrush(ArgbToColor(sh.StrokeArgb)), sh.StrokeWidth)
        {
            LineJoin = PenLineJoin.Round,
        };

        double x = sh.BoundsX, y = sh.BoundsY, w = sh.BoundsWidth, h = sh.BoundsHeight;

        switch (sh.Type)
        {
            case ShapeType.Rectangle:
                dc.DrawRectangle(fill, stroke, new Rect(x, y, w, h));
                break;

            case ShapeType.Circle:
                dc.DrawEllipse(fill, stroke,
                    new Point(x + w / 2, y + h / 2),
                    radiusX: w / 2,
                    radiusY: h / 2);
                break;

            case ShapeType.Line:
                dc.DrawLine(stroke, new Point(x, y + h / 2), new Point(x + w, y + h / 2));
                break;

            case ShapeType.Arrow:
                RenderArrow(dc, stroke, new Point(x, y + h / 2), new Point(x + w, y + h / 2));
                break;

            case ShapeType.Triangle:
                var triGeo = new StreamGeometry();
                using (var ctx = triGeo.Open())
                {
                    ctx.BeginFigure(new Point(x + w / 2, y), true, true);
                    ctx.LineTo(new Point(x + w, y + h), true, false);
                    ctx.LineTo(new Point(x, y + h), true, false);
                }
                triGeo.Freeze();
                dc.DrawGeometry(fill, stroke, triGeo);
                break;

            default:
                dc.DrawRectangle(fill, stroke, new Rect(x, y, w, h));
                break;
        }
    }

    private static void RenderArrow(DrawingContext dc, Pen pen, Point from, Point to)
    {
        dc.DrawLine(pen, from, to);

        // 箭头头部：以终点为顶，两侧各展开 20° 绘制两条短线
        var dir   = to - from;
        var len   = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
        if (len < 1) return;
        var unit  = new Vector(dir.X / len, dir.Y / len);
        double hs = Math.Min(len * 0.25, 20); // 箭头长度
        const double angle = Math.PI / 6;     // 30°

        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        var left  = new Vector(-unit.X * cos - unit.Y * sin, -unit.X * sin + unit.Y * cos) * hs;
        var right = new Vector(-unit.X * cos + unit.Y * sin,  unit.X * sin + unit.Y * cos) * hs;

        dc.DrawLine(pen, to, to + left);
        dc.DrawLine(pen, to, to + right);
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
