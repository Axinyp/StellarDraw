using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WhiteBoard.AI.Abstractions;

namespace WhiteBoard.AI.Math;

/// <summary>
/// 基于 WPF 内置渲染 API 的数学表达式渲染器。
/// 使用 FormattedText + DrawingVisual + RenderTargetBitmap 将表达式渲染为 PNG 字节数组。
/// 支持 Cambria Math 字体（含数学符号），对 LaTeX 语法提供有限支持（纯文本展示）。
/// 如需完整 LaTeX 渲染，可替换为 WpfMath（WPFMath NuGet）的自定义实现。
/// </summary>
public sealed class WpfMathRenderer : IMathRenderer
{
    private const double DpiValue = 96.0;
    private const double PixelsPerDip = 1.0;   // 96 DPI → 1.0

    public bool IsAvailable => true;

    public byte[] Render(string latex, double fontSize = 24.0)
    {
        // RenderTargetBitmap requires STA thread
        byte[]? result = null;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try { result = RenderCore(latex, fontSize); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (error is not null)
            throw new InvalidOperationException($"数学表达式渲染失败: {error.Message}", error);

        return result!;
    }

    private static byte[] RenderCore(string expression, double fontSize)
    {
        // 将 LaTeX 控制序列转为可读文本（简单映射，非完整 LaTeX 解析）
        var displayText = SimplifyLatex(expression);

        var typeface = new Typeface(
            new FontFamily("Cambria Math, Segoe UI Symbol, Arial"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        var formatted = new FormattedText(
            displayText,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            PixelsPerDip);

        const int padding = 16;
        var bmpWidth = System.Math.Max((int)(formatted.Width + padding * 2), 80);
        var bmpHeight = System.Math.Max((int)(formatted.Height + padding * 2), 40);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, bmpWidth, bmpHeight));
            dc.DrawText(formatted, new Point(padding, padding));
        }

        var bitmap = new RenderTargetBitmap(
            bmpWidth, bmpHeight, DpiValue, DpiValue, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    // 将常见 LaTeX 命令转换为 Unicode 可显示字符（有限支持）
    private static string SimplifyLatex(string latex)
        => latex
            .Replace(@"\frac{", "(")
            .Replace(@"\sqrt{", "√(")
            .Replace(@"\pi", "π")
            .Replace(@"\alpha", "α")
            .Replace(@"\beta", "β")
            .Replace(@"\gamma", "γ")
            .Replace(@"\delta", "δ")
            .Replace(@"\theta", "θ")
            .Replace(@"\infty", "∞")
            .Replace(@"\times", "×")
            .Replace(@"\div", "÷")
            .Replace(@"\pm", "±")
            .Replace(@"\neq", "≠")
            .Replace(@"\leq", "≤")
            .Replace(@"\geq", "≥")
            .Replace(@"\sum", "Σ")
            .Replace(@"\int", "∫")
            .Replace(@"\cdot", "·")
            .Replace(@"\left(", "(").Replace(@"\right)", ")")
            .Replace(@"\left[", "[").Replace(@"\right]", "]")
            .Replace("}{", "/").Replace("{", "").Replace("}", "")
            .Replace("$", "")
            .Trim();
}