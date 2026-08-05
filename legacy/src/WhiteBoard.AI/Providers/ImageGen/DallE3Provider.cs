using OpenAI;
using OpenAI.Images;
using System.ClientModel;
using WhiteBoard.AI.Abstractions;

namespace WhiteBoard.AI.Providers.ImageGen;

/// <summary>
/// 基于 OpenAI DALL-E 3 API 的图像生成提供商。
/// 与 LlmProviderFactory 共用 OpenAI client，但端点和模型独立配置。
/// </summary>
public sealed class DallE3Provider : IImageGenProvider
{
    private readonly ImageClient _client;
    private readonly bool _hasKey;

    public string ProviderName => "dall-e-3";

    // 无 API Key 时视为不可用，避免空 Key 也注册工具、调用时必然 401 失败。
    public bool IsAvailable => _hasKey;

    /// <param name="apiKey">OpenAI / 兼容 API 密钥。</param>
    /// <param name="endpoint">自定义端点（null 则使用 OpenAI 官方）。</param>
    public DallE3Provider(string apiKey, Uri? endpoint = null)
    {
        _hasKey = !string.IsNullOrWhiteSpace(apiKey);

        var options = endpoint is null
            ? null
            : new OpenAIClientOptions { Endpoint = endpoint };

        var openAiClient = options is null
            ? new OpenAIClient(new ApiKeyCredential(apiKey))
            : new OpenAIClient(new ApiKeyCredential(apiKey), options);

        _client = openAiClient.GetImageClient("dall-e-3");
    }

    public async Task<byte[]> GenerateAsync(
        string prompt,
        ImageGenStyle style = ImageGenStyle.Realistic,
        int width = 512,
        int height = 512,
        CancellationToken ct = default)
    {
        // DALL-E 3 支持的尺寸：1024×1024、1792×1024、1024×1792
        var size = (width > height * 1.2) ? GeneratedImageSize.W1792xH1024
                 : (height > width * 1.2) ? GeneratedImageSize.W1024xH1792
                 : GeneratedImageSize.W1024xH1024;

        var enhancedPrompt = style switch
        {
            ImageGenStyle.Diagram => $"Technical diagram, clean lines, white background, precise labels. {prompt}",
            ImageGenStyle.Cartoon => $"Colorful cartoon illustration, friendly style. {prompt}",
            ImageGenStyle.Sketch => $"Hand-drawn pencil sketch, simple lines, monochrome. {prompt}",
            _ => prompt,
        };

        var result = await _client.GenerateImageAsync(
            enhancedPrompt,
            new ImageGenerationOptions
            {
                ResponseFormat = GeneratedImageFormat.Bytes,
                Size = size,
                Quality = GeneratedImageQuality.Standard,
            },
            ct).ConfigureAwait(false);

        return result.Value.ImageBytes.ToArray();
    }
}