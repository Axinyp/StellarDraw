using Microsoft.Extensions.AI;
using WhiteBoard.AI.Services;

namespace WhiteBoard.AI.Tools.Catalogs;

/// <summary>绘图工具集：在白板上插入形状。</summary>
public static class DrawingToolCatalog
{
    public static void Register(WhiteboardToolRegistry registry, IWhiteboardAiActions actions)
    {
        registry.Register(
            AIFunctionFactory.Create(
                async (
                    string shapeType,
                    float x, float y, float width, float height,
                    string fillColor   = "#FFFFFF",
                    string strokeColor = "#000000",
                    float  strokeWidth = 2f,
                    bool   isFilled    = true,
                    string? alias      = null,
                    string? groupId    = null,
                    CancellationToken ct = default) =>
                {
                    var spec = new ShapeSpec(shapeType, x, y, width, height,
                        fillColor, strokeColor, strokeWidth, isFilled);
                    return (await actions.InsertShapeAsync(spec, alias, groupId, ct)).ToJson();
                },
                "insert_shape",
                "在白板上插入一个形状。shapeType 可选值: rectangle(矩形), circle(圆形), " +
                "line(直线), arrow(箭头), triangle(三角形)。" +
                "坐标单位为像素，画布左上角为原点(0,0)；调用 get_canvas_size 可获取当前画布右下角坐标。" +
                "fillColor/strokeColor 为十六进制颜色字符串(如 #FF0000)。" +
                "可选 alias 参数为该对象命名，后续操作可用别名引用。"),
            ToolCapabilityInfo.Safe());
    }
}
