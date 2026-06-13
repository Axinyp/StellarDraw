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
                "【仅限标准几何形状】在白板上插入简单几何形状。shapeType 可选值: rectangle(矩形), circle(圆形), " +
                "line(直线), arrow(箭头), triangle(三角形)。" +
                "不适用于复杂图像、角色、动物——那类内容请用 generate_and_insert_image。" +
                "坐标单位为像素，画布左上角为原点(0,0)；调用 get_canvas_size 可获取当前画布右下角坐标。" +
                "fillColor/strokeColor 为十六进制颜色字符串(如 #FF0000)。" +
                "可选 alias 参数为该对象命名，后续操作可用别名引用。"),
            ToolCapabilityInfo.Safe());

        registry.Register(
            AIFunctionFactory.Create(
                async (
                    string pathData,
                    string fillColor   = "#FFD700",
                    string strokeColor = "#000000",
                    float  strokeWidth = 2f,
                    bool   isFilled    = true,
                    string? alias      = null,
                    string? groupId    = null,
                    CancellationToken ct = default) =>
                {
                    var spec = new PathSpec(pathData, fillColor, strokeColor, strokeWidth, isFilled);
                    return (await actions.InsertPathAsync(spec, alias, groupId, ct)).ToJson();
                },
                "draw_path",
                "【仅限几何图形/图标】用 SVG path string 绘制任意矢量形状（多边形、星形、箭头、流程图元素、简单图标轮廓）。" +
                "不适用于复杂图像、角色、动物或写实场景——那类内容请用 generate_and_insert_image。" +
                "pathData 使用标准 SVG path 语法：M(移动) L(直线) C(三阶贝塞尔) Q(二阶贝塞尔) A(弧) Z(闭合)。" +
                "坐标单位为像素，画布左上角为原点(0,0)。" +
                "五角星示例（中心960,540，外径300）：\"M 960 240 L 1025 450 L 1245 450 L 1070 575 L 1135 790 L 960 665 L 785 790 L 850 575 L 675 450 L 895 450 Z\"。" +
                "fillColor/strokeColor 为十六进制颜色(如 #FFD700 金色)；isFilled=true 时填充内部。"),
            ToolCapabilityInfo.Safe());
    }
}
