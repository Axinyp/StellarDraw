using WhiteBoard.AI.Abstractions;
using WhiteBoard.AI.Services;

namespace WhiteBoard.AI.Math;

/// <summary>
/// 数学解题步骤写入器。
/// 将 LLM 返回的解题步骤列表逐步插入白板，支持 LaTeX 步骤渲染为图片和纯文本降级。
/// 步骤间设有短暂延迟以产生逐步动画效果。
/// </summary>
public sealed class MathStepWriter
{
    private readonly IWhiteboardAiActions _actions;
    private readonly IMathRenderer? _renderer;

    public MathStepWriter(IWhiteboardAiActions actions, IMathRenderer? renderer = null)
    {
        _actions = actions;
        _renderer = renderer;
    }

    /// <summary>
    /// 将解题步骤列表逐步写入白板。
    /// </summary>
    /// <param name="steps">解题步骤数组（支持 LaTeX 格式，如 \frac{a}{b}）。</param>
    /// <param name="startX">起始 X 坐标（像素）。</param>
    /// <param name="startY">起始 Y 坐标（像素）。</param>
    /// <param name="columnWidth">每步最大宽度（像素）。</param>
    /// <param name="rowHeight">每步高度（像素）。</param>
    /// <param name="groupId">撤销分组 ID，全部步骤作为一个整体可撤销。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task WriteStepsAsync(
        IReadOnlyList<string> steps,
        float startX = 100f,
        float startY = 100f,
        float columnWidth = 500f,
        float rowHeight = 60f,
        string? groupId = null,
        CancellationToken ct = default)
    {
        var y = startY;

        foreach (var step in steps)
        {
            ct.ThrowIfCancellationRequested();

            await WriteStepAsync(step, startX, y, columnWidth, rowHeight, groupId, ct);

            y += rowHeight + 10f;
            await Task.Delay(200, ct); // 逐步动画延迟
        }
    }

    private async Task WriteStepAsync(
        string step, float x, float y, float w, float h,
        string? groupId, CancellationToken ct)
    {
        if (_renderer?.IsAvailable == true && LooksLikeLatex(step))
        {
            try
            {
                var pngBytes = _renderer.Render(step);
                var spec = new ImageSpec(x, y, w, h, pngBytes);
                await _actions.InsertImageAsync(spec, groupId: groupId, ct: ct);
                return;
            }
            catch
            {
                // 降级到纯文本
            }
        }

        var textSpec = new TextSpec(x, y, w, h, step);
        await _actions.InsertTextAsync(textSpec, groupId: groupId, ct: ct);
    }

    private static bool LooksLikeLatex(string text)
        => text.Contains('\\') || CountChar(text, '$') >= 2;

    private static int CountChar(string s, char c)
    {
        int count = 0;
        foreach (var ch in s)
            if (ch == c) count++;
        return count;
    }
}