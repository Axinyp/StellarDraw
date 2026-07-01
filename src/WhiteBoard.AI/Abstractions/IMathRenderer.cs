namespace WhiteBoard.AI.Abstractions;

/// <summary>
/// LaTeX 公式渲染抽象。实现类通过 WpfMath 将 LaTeX 字符串渲染为 PNG 字节数组。
/// 返回 PNG bytes 而非 BitmapSource，保持 WhiteBoard.AI 程序集平台无关。
/// 未配置时 MathToolCatalog 中的工具不会注册。
/// </summary>
public interface IMathRenderer
{
    /// <summary>将 LaTeX 表达式渲染为 PNG 字节数组，fontSize 单位为 pt。</summary>
    byte[] Render(string latex, double fontSize = 24.0);

    bool IsAvailable { get; }
}