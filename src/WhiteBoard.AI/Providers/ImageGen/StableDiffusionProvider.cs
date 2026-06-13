using System.Net.Http;
using System.Text;
using System.Text.Json;
using WhiteBoard.AI.Abstractions;

namespace WhiteBoard.AI.Providers.ImageGen;

/// <summary>
/// 基于 Stable Diffusion WebUI (Automatic1111) REST API 的本地图像生成提供商。
/// 默认端点：http://localhost:7860（与 SD WebUI 默认端口一致）。
/// IsAvailable 通过探测 /sdapi/v1/progress 端点动态检测服务是否在线。
/// </summary>
public sealed class StableDiffusionProvider : IImageGenProvider, IDisposable
{
    private readonly HttpClient _http;
    private readonly string     _endpoint;
    private bool?               _cachedAvailable;

    public string ProviderName => "stable-diffusion";

    public bool IsAvailable
    {
        get
        {
            if (_cachedAvailable.HasValue) return _cachedAvailable.Value;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var resp = _http.GetAsync($"{_endpoint}/sdapi/v1/sd-models", cts.Token)
                               .GetAwaiter().GetResult();
                _cachedAvailable = resp.IsSuccessStatusCode;
            }
            catch
            {
                _cachedAvailable = false;
            }
            return _cachedAvailable.Value;
        }
    }

    public StableDiffusionProvider(string endpoint = "http://localhost:7860")
    {
        _endpoint = endpoint.TrimEnd('/');
        _http     = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public async Task<byte[]> GenerateAsync(
        string            prompt,
        ImageGenStyle     style  = ImageGenStyle.Realistic,
        int               width  = 512,
        int               height = 512,
        CancellationToken ct     = default)
    {
        var (stylePrompt, negPrompt) = style switch
        {
            ImageGenStyle.Diagram => (", technical diagram, blueprint, clean linework, white background",
                                      "photo, painting, 3d render, people"),
            ImageGenStyle.Cartoon => (", cartoon style, anime, colorful, vibrant",
                                      "photo-realistic, dark, monochrome"),
            ImageGenStyle.Sketch  => (", pencil sketch, hand drawn, black and white, hatching",
                                      "color, 3d, photo"),
            _                    => ("", "blurry, deformed, ugly, watermark"),
        };

        var requestBody = new
        {
            prompt          = prompt + stylePrompt,
            negative_prompt = negPrompt,
            width           = System.Math.Clamp(width,  64, 2048),
            height          = System.Math.Clamp(height, 64, 2048),
            steps           = 20,
            sampler_name    = "DPM++ 2M Karras",
            cfg_scale       = 7,
            batch_size      = 1,
        };

        var json    = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http
            .PostAsync($"{_endpoint}/sdapi/v1/txt2img", content, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc    = JsonDocument.Parse(responseJson);
        var base64       = doc.RootElement.GetProperty("images")[0].GetString()
            ?? throw new InvalidOperationException("SD API 返回了空图像数据");

        return Convert.FromBase64String(base64);
    }

    public void Dispose() => _http.Dispose();
}
