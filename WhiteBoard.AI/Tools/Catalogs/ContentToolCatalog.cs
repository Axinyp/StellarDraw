using Microsoft.Extensions.AI;
using WhiteBoard.AI.Services;

namespace WhiteBoard.AI.Tools.Catalogs;

/// <summary>内容工具集：插入文字、图片等富内容。</summary>
public static class ContentToolCatalog
{
    public static void Register(WhiteboardToolRegistry registry, IWhiteboardAiActions actions)
    {
        registry.Register(
            AIFunctionFactory.Create(
                async (
                    float   x,
                    float   y,
                    float   width,
                    float   height,
                    string  content,
                    string  fontFamily  = "Microsoft YaHei",
                    float   fontSize    = 16f,
                    string  color       = "#000000",
                    bool    bold        = false,
                    bool    italic      = false,
                    string? alias       = null,
                    string? groupId     = null,
                    CancellationToken ct = default) =>
                {
                    var spec = new TextSpec(x, y, width, height, content,
                        fontFamily, fontSize, color, bold, italic);
                    return (await actions.InsertTextAsync(spec, alias, groupId, ct)).ToJson();
                },
                "insert_text",
                "在白板上插入一段文字。content 为文字内容，支持换行符 \\n。" +
                "fontSize 单位为像素。color 为十六进制颜色（如 #000000）。" +
                "width/height 为文本框尺寸，建议预留足够空间防止截断。"),
            ToolCapabilityInfo.Safe());
    }
}
