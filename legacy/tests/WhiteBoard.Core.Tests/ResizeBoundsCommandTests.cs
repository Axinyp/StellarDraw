using WhiteBoard.Contracts.Services;
using WhiteBoard.Core.Commands;
using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;

namespace WhiteBoard.Core.Tests;

public class ResizeBoundsCommandTests
{
    private static StrokeObject MakeStroke()
    {
        var style = new StrokeStyle(PenType.Pen, InkType.Normal, 0xFF000000, 4f);
        // points [100..200], thickness 4 → bounds (98,98,104,104), transform=identity
        var pts = new[]
        {
            new PressurePoint(100, 100, 0.5f, 0),
            new PressurePoint(200, 200, 0.5f, 10),
        };
        return new StrokeObject(pts, style);
    }

    // 渲染时点的实际文档坐标 = Transform(行向量) × point
    private static (float minX, float minY, float maxX, float maxY) RenderedAabb(StrokeObject s)
    {
        var t = s.Transform;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in s.Points)
        {
            float dx = t[0] * p.X + t[2] * p.Y + t[4];
            float dy = t[1] * p.X + t[3] * p.Y + t[5];
            minX = Math.Min(minX, dx); maxX = Math.Max(maxX, dx);
            minY = Math.Min(minY, dy); maxY = Math.Max(maxY, dy);
        }
        return (minX, minY, maxX, maxY);
    }

    [Fact]
    public void Resize_BoundsMatchesRenderedGeometry()
    {
        var page = new PageModel("p1", "Test");
        var stroke = MakeStroke();

        // 第一次放大 2x
        new ResizeBoundsCommand([stroke], [(98f, 98f, 208f, 208f)], page).Execute();
        var a1 = RenderedAabb(stroke);

        // 第二次再放大 2x
        new ResizeBoundsCommand([stroke], [(98f, 98f, 416f, 416f)], page).Execute();
        var a2 = RenderedAabb(stroke);

        // 渲染几何中心应与 Bounds 中心基本重合（容差 pad 量级）
        float boundsCx = stroke.BoundsX + stroke.BoundsWidth / 2;
        float boundsCy = stroke.BoundsY + stroke.BoundsHeight / 2;
        float renderCx = (a2.minX + a2.maxX) / 2;
        float renderCy = (a2.minY + a2.maxY) / 2;

        Assert.True(
            Math.Abs(boundsCx - renderCx) < 5 && Math.Abs(boundsCy - renderCy) < 5,
            $"resize1 rendered={a1}; resize2 rendered={a2}; " +
            $"bounds=({stroke.BoundsX},{stroke.BoundsY},{stroke.BoundsWidth},{stroke.BoundsHeight}); " +
            $"boundsCenter=({boundsCx},{boundsCy}) renderCenter=({renderCx},{renderCy})");
    }

    [Fact]
    public void RepeatedResize_BoundsStayTightAroundGeometry()
    {
        // 复现「多次缩放后选中框越来越大于对象」：反复放大，检查 Bounds 始终紧贴渲染几何
        var page = new PageModel("p1", "Test");
        var stroke = MakeStroke(); // points 100..200, thickness 4

        // 连续放大 5 次，每次基于当前 Bounds 放大 1.5x（模拟用户反复拖手柄）
        for (int i = 0; i < 5; i++)
        {
            float x = stroke.BoundsX, y = stroke.BoundsY;
            float w = stroke.BoundsWidth * 1.5f, h = stroke.BoundsHeight * 1.5f;
            new ResizeBoundsCommand([stroke], [(x, y, w, h)], page).Execute();
        }

        var aabb = RenderedAabb(stroke);
        float renderW = aabb.maxX - aabb.minX;
        float renderH = aabb.maxY - aabb.minY;
        float pad = 2f; // thickness/2

        // Bounds 应紧贴渲染几何（差距约一个笔宽），而非因 pad 累积膨胀
        Assert.True(
            stroke.BoundsWidth - renderW < 3 * 4f && stroke.BoundsHeight - renderH < 3 * 4f,
            $"bounds=({stroke.BoundsWidth}x{stroke.BoundsHeight}) renderedGeom=({renderW}x{renderH}); " +
            $"差距过大说明 pad 累积膨胀（选中框比对象大）");
    }
}

