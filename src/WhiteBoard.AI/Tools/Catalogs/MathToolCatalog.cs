using Microsoft.Extensions.AI;
using WhiteBoard.AI.Abstractions;
using WhiteBoard.AI.Services;
using MathStepWriter = WhiteBoard.AI.Math.MathStepWriter;

namespace WhiteBoard.AI.Tools.Catalogs;

/// <summary>图表数据点：标签 + 数值。</summary>
public sealed record ChartItem(string Label, double Value);

/// <summary>
/// 数学/可视化工具集：LaTeX 渲染、解题步骤、数据图表。
/// 仅在 IMathRenderer.IsAvailable == true 时由 WhiteboardToolRegistry 注册。
/// </summary>
public static class MathToolCatalog
{
    private static readonly string[] BarColors =
    [
        "#4472C4", "#ED7D31", "#A9D18E", "#FFC000", "#4BACC6",
        "#FF0000", "#7030A0", "#00B050", "#FF7C00", "#0070C0",
    ];

    public static void Register(
        WhiteboardToolRegistry registry,
        IWhiteboardAiActions actions,
        IMathRenderer renderer)
    {
        var stepWriter = new MathStepWriter(actions, renderer);

        // ── render_latex ───────────────────────────────────────────────────────
        registry.Register(
            AIFunctionFactory.Create(
                async (
                    string expression,
                    float x,
                    float y,
                    float fontSize = 24f,
                    float maxWidth = 600f,
                    string? alias = null,
                    CancellationToken ct = default) =>
                {
                    var pngBytes = renderer.Render(expression, fontSize);
                    var height = System.Math.Max(fontSize * 3f, 80f);
                    var spec = new ImageSpec(x, y, maxWidth, height, pngBytes);
                    return (await actions.InsertImageAsync(spec, alias, ct: ct)).ToJson();
                },
                "render_latex",
                "将 LaTeX 数学公式渲染为图片并插入白板。" +
                "expression 为标准 LaTeX 表达式，不含 $ 定界符，" +
                "例如 \\frac{a}{b} 或 x = \\frac{-b \\pm \\sqrt{b^2-4ac}}{2a}。" +
                "fontSize 为渲染字号（pt），maxWidth 为最大宽度（像素）。"),
            ToolCapabilityInfo.Safe());

        // ── solve_and_show ─────────────────────────────────────────────────────
        registry.Register(
            AIFunctionFactory.Create(
                async (
                    IReadOnlyList<string> steps,
                    float startX = 100f,
                    float startY = 100f,
                    float rowHeight = 60f,
                    string? groupId = null,
                    CancellationToken ct = default) =>
                {
                    await stepWriter.WriteStepsAsync(
                        steps, startX, startY,
                        columnWidth: 600f, rowHeight: rowHeight,
                        groupId: groupId, ct: ct);

                    return ToolResult.Ok(groupId: groupId).ToJson();
                },
                "solve_and_show",
                "将数学解题步骤逐步写入白板（产生逐步动画效果）。" +
                "steps 为 LLM 预先计算好的步骤字符串数组，每个元素为一步，支持 LaTeX 格式。" +
                "示例：steps=[\"2x^2-5x+3=0\", \"因式分解: (2x-3)(x-1)=0\", \"解: x=3/2 或 x=1\"]。"),
            ToolCapabilityInfo.Safe());

        // ── draw_chart ─────────────────────────────────────────────────────────
        registry.Register(
            AIFunctionFactory.Create(
                async (
                    string type,
                    IReadOnlyList<ChartItem> data,
                    float x = 100f,
                    float y = 100f,
                    float width = 600f,
                    float height = 400f,
                    string? groupId = null,
                    CancellationToken ct = default) =>
                {
                    var result = type.ToLowerInvariant() switch
                    {
                        "bar" => await DrawBarChartAsync(actions, data, x, y, width, height, groupId, ct),
                        "line" => await DrawLineChartAsync(actions, data, x, y, width, height, groupId, ct),
                        _ => ToolResult.Error($"不支持的图表类型 '{type}'，可选值：bar（柱状图）/ line（折线图）"),
                    };
                    return result.ToJson();
                },
                "draw_chart",
                "在白板上绘制简单数据图表。" +
                "type 可选 bar（柱状图）或 line（折线图）。" +
                "data 为数据点数组，每项含 label（字符串）和 value（数值）。" +
                "x/y 为图表左上角坐标（像素），width/height 为图表尺寸（像素）。"),
            ToolCapabilityInfo.Safe());
    }

    // ── Bar Chart ─────────────────────────────────────────────────────────────

    private static async Task<ToolResult> DrawBarChartAsync(
        IWhiteboardAiActions actions,
        IReadOnlyList<ChartItem> data,
        float x, float y, float width, float height,
        string? groupId, CancellationToken ct)
    {
        if (data.Count == 0) return ToolResult.Error("数据为空，无法绘制柱状图");

        double maxValue = data.Max(d => d.Value);
        if (maxValue <= 0) return ToolResult.Error("数据值必须大于零");

        var slotWidth = width / data.Count;
        var barWidth = slotWidth * 0.65f;
        var chartHeight = height * 0.8f;
        var baseY = y + height - height * 0.15f;
        var created = new List<string>();

        for (int i = 0; i < data.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var barHeight = (float)(data[i].Value / maxValue * chartHeight);
            var barX = x + i * slotWidth + (slotWidth - barWidth) / 2f;
            var barY = baseY - barHeight;
            var color = BarColors[i % BarColors.Length];

            var barResult = await actions.InsertShapeAsync(
                new ShapeSpec("rectangle", barX, barY, barWidth, barHeight,
                    color, color, 1f, true),
                groupId: groupId, ct: ct);
            created.AddRange(barResult.CreatedObjectIds);

            var lblResult = await actions.InsertTextAsync(
                new TextSpec(barX, baseY + 4f, barWidth, 20f, data[i].Label, FontSize: 12f),
                groupId: groupId, ct: ct);
            created.AddRange(lblResult.CreatedObjectIds);
        }

        return ToolResult.Ok(created: created, groupId: groupId);
    }

    // ── Line Chart ────────────────────────────────────────────────────────────

    private static async Task<ToolResult> DrawLineChartAsync(
        IWhiteboardAiActions actions,
        IReadOnlyList<ChartItem> data,
        float x, float y, float width, float height,
        string? groupId, CancellationToken ct)
    {
        if (data.Count < 2) return ToolResult.Error("折线图至少需要 2 个数据点");

        double maxValue = data.Max(d => d.Value);
        if (maxValue <= 0) return ToolResult.Error("数据值必须大于零");

        var chartHeight = height * 0.8f;
        var baseY = y + height - height * 0.15f;
        var stepX = width / (data.Count - 1);
        var created = new List<string>();

        // 预计算各点坐标
        var pts = new (float px, float py)[data.Count];
        for (int i = 0; i < data.Count; i++)
            pts[i] = (x + i * stepX, baseY - (float)(data[i].Value / maxValue * chartHeight));

        // 折线段
        for (int i = 0; i < data.Count - 1; i++)
        {
            ct.ThrowIfCancellationRequested();

            var (x1, y1) = pts[i];
            var (x2, y2) = pts[i + 1];

            var lx = System.Math.Min(x1, x2);
            var ly = System.Math.Min(y1, y2);
            var lw = System.Math.Max(System.Math.Abs(x2 - x1), 1f);
            var lh = System.Math.Max(System.Math.Abs(y2 - y1), 1f);

            var lineResult = await actions.InsertShapeAsync(
                new ShapeSpec("line", lx, ly, lw, lh, "#4472C4", "#4472C4", 3f, false),
                groupId: groupId, ct: ct);
            created.AddRange(lineResult.CreatedObjectIds);
        }

        // 数据点标记 + 标签
        var half = stepX / 2f;
        for (int i = 0; i < data.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var (px, py) = pts[i];

            var dotResult = await actions.InsertShapeAsync(
                new ShapeSpec("circle", px - 5f, py - 5f, 10f, 10f,
                    "#4472C4", "#FFFFFF", 2f, true),
                groupId: groupId, ct: ct);
            created.AddRange(dotResult.CreatedObjectIds);

            var lblResult = await actions.InsertTextAsync(
                new TextSpec(px - half, baseY + 4f, stepX, 20f, data[i].Label, FontSize: 11f),
                groupId: groupId, ct: ct);
            created.AddRange(lblResult.CreatedObjectIds);
        }

        return ToolResult.Ok(created: created, groupId: groupId);
    }
}