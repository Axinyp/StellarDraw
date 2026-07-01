using WhiteBoard.AI.Abstractions;
using WhiteBoard.AI.Settings;

namespace WhiteBoard.AI.Providers.ImageGen;

/// <summary>
/// 根据 <see cref="AIWhiteboardSettings"/> 构建图像生成 Provider。
/// 未启用（ImageGenEnabled=false）或类型未知时返回 null —— null 会让
/// <c>WhiteboardToolRegistry</c> 跳过 generate_and_insert_image 工具注册。
/// </summary>
public static class ImageGenProviderFactory
{
    public static IImageGenProvider? Create(AIWhiteboardSettings s, ISecretStore secrets)
    {
        if (!s.ImageGenEnabled) return null;

        return s.ImageGenProviderType.ToLowerInvariant() switch
        {
            "dalle3" => CreateDalle(s, secrets),

            "stablediffusion" => new StableDiffusionProvider(
                string.IsNullOrWhiteSpace(s.ImageGenEndpoint)
                    ? "http://localhost:7860"
                    : s.ImageGenEndpoint),

            _ => null, // 未知 provider → 静默忽略，工具不注册
        };
    }

    private static DallE3Provider CreateDalle(AIWhiteboardSettings s, ISecretStore secrets)
    {
        var apiKey = string.IsNullOrWhiteSpace(s.ImageGenApiKeyRef)
            ? "" : secrets.Load(s.ImageGenApiKeyRef);

        // 自定义端点（如 OpenAI 兼容代理）；留空则用官方端点
        Uri? endpoint = string.IsNullOrWhiteSpace(s.ImageGenEndpoint)
            ? null : new Uri(s.ImageGenEndpoint);

        return new DallE3Provider(apiKey ?? "", endpoint);
    }
}
