namespace WhiteBoard.AI.Abstractions;

/// <summary>
/// 图像生成提供商抽象。与 ILlmProvider 完全独立，可单独配置和切换。
/// IsAvailable = false 时，ImageGenToolCatalog 中的工具不会注册到 WhiteboardToolRegistry。
/// </summary>
public interface IImageGenProvider
{
    string ProviderName { get; }
    bool   IsAvailable  { get; }

    Task<byte[]> GenerateAsync(
        string            prompt,
        ImageGenStyle     style  = ImageGenStyle.Realistic,
        int               width  = 512,
        int               height = 512,
        CancellationToken ct     = default);
}

public enum ImageGenStyle
{
    Realistic,
    Diagram,
    Cartoon,
    Sketch,
}
