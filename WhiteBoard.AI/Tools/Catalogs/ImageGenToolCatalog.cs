using Microsoft.Extensions.AI;
using WhiteBoard.AI.Abstractions;
using WhiteBoard.AI.Services;

namespace WhiteBoard.AI.Tools.Catalogs;

/// <summary>
/// 图像生成工具集：调用 IImageGenProvider 生成图片并插入白板。
/// 仅在 IImageGenProvider.IsAvailable == true 时由 WhiteboardToolRegistry 注册。
/// </summary>
public static class ImageGenToolCatalog
{
    public static void Register(
        WhiteboardToolRegistry registry,
        IWhiteboardAiActions   actions,
        IImageGenProvider      imageGen)
    {
        registry.Register(
            AIFunctionFactory.Create(
                async (
                    string  prompt,
                    float   x,
                    float   y,
                    float   width      = 400f,
                    float   height     = 300f,
                    string  style      = "realistic",
                    string? alias      = null,
                    string? groupId    = null,
                    CancellationToken ct = default) =>
                {
                    var genStyle = style.ToLowerInvariant() switch
                    {
                        "diagram" => ImageGenStyle.Diagram,
                        "cartoon" => ImageGenStyle.Cartoon,
                        "sketch"  => ImageGenStyle.Sketch,
                        _         => ImageGenStyle.Realistic,
                    };

                    var imageBytes = await imageGen.GenerateAsync(
                        prompt, genStyle, (int)width, (int)height, ct)
                        .ConfigureAwait(false);

                    var spec = new ImageSpec(x, y, width, height, imageBytes);
                    return (await actions.InsertImageAsync(spec, alias, groupId, ct)).ToJson();
                },
                "generate_and_insert_image",
                "使用 AI 图像生成服务创建图片并插入白板指定位置。" +
                "prompt 为生成描述（英文效果更佳）；" +
                "style 可选 realistic（写实）/diagram（图表）/cartoon（卡通）/sketch（素描）；" +
                "x/y 为插入坐标（像素），width/height 为图片尺寸。" +
                "返回 objectId，可用 alias 为其命名供后续操作引用。"),
            ToolCapabilityInfo.Safe());
    }
}
