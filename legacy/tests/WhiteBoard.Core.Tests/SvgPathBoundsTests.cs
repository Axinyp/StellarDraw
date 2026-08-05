using WhiteBoard.Core.Algorithms;

namespace WhiteBoard.Core.Tests;

public class SvgPathBoundsTests
{
    private static void AssertBounds(string path, float x, float y, float w, float h, float tol = 0.6f)
    {
        var (bx, by, bw, bh) = SvgPathBounds.Compute(path);
        Assert.True(
            Math.Abs(bx - x) < tol && Math.Abs(by - y) < tol &&
            Math.Abs(bw - w) < tol && Math.Abs(bh - h) < tol,
            $"path='{path}' 期望=({x},{y},{w},{h}) 实际=({bx},{by},{bw},{bh})");
    }

    [Fact]
    public void AbsoluteLines_Triangle()
        => AssertBounds("M 100 100 L 300 100 L 200 300 Z", 100, 100, 200, 200);

    [Fact]
    public void CompactAbsolute()
        => AssertBounds("M100,100L300,100L200,300Z", 100, 100, 200, 200);

    [Fact]
    public void RelativeCommands_MapToSameAbsoluteBox()
        // m100,100 → (100,100); l200,0 → (300,100); l-100,200 → (200,300); z
        => AssertBounds("m 100 100 l 200 0 l -100 200 z", 100, 100, 200, 200);

    [Fact]
    public void HorizontalVertical()
        // (10,10) H110 → (110,10) V60 → (110,60)
        => AssertBounds("M 10 10 H 110 V 60", 10, 10, 100, 50);

    [Fact]
    public void CubicBezier_UsesExtremaNotControlPoints()
        // 控制点 y=100，但曲线 y 极值仅 75；旧实现会把控制点当坐标算成 h=100
        => AssertBounds("M 0 0 C 0 100 100 100 100 0", 0, 0, 100, 75);

    [Fact]
    public void QuadraticBezier()
        // Q 控制点 (50,100)，曲线 y 极值 = 50
        => AssertBounds("M 0 0 Q 50 100 100 0", 0, 0, 100, 50);

    [Fact]
    public void EmptyOrInvalid_ReturnsZero()
        => AssertBounds("", 0, 0, 0, 0);
}
