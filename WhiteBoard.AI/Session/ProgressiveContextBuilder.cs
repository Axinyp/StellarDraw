using WhiteBoard.Core.Domain;

namespace WhiteBoard.AI.Session;

/// <summary>
/// 渐进式上下文构建器（4 级）。
/// Level 0 的 system prompt 始终注入；更高层级由 AI 主动调用 Query 工具获取。
/// </summary>
public sealed class ProgressiveContextBuilder
{
    private readonly WhiteboardSession _session;

    public ProgressiveContextBuilder(WhiteboardSession session) => _session = session;

    /// <summary>构建 Level 0 system prompt，始终注入到每次 LLM 调用的消息列表开头。</summary>
    public string BuildLevel0(ExecutionMode mode, IReadOnlyList<string> availableTools)
    {
        var page    = _session.Current;
        var history = _session.History;
        var tools   = string.Join(", ", availableTools);

        return $"""
            你是一个智能白板 AI 助手，可以操控白板上的所有功能。

            ## 当前状态
            - 当前页面: {page.Id} (共 {_session.Pages.AllPages.Count} 页)
            - 可撤销: {(history.CanUndo ? "是" : "否")}
            - 可重做: {(history.CanRedo ? "是" : "否")}
            - 执行模式: {(mode == ExecutionMode.ConfirmRequired ? "破坏性操作需确认" : "全自动")}

            ## 可用工具
            {tools}

            ## 坐标系
            - 画布宽 1920px，高 1080px，左上角为原点 (0, 0)
            - 所有坐标单位为像素

            ## 操作规则
            - 批量操作时，先调用 get_canvas_size 确认画布范围
            - 引用已创建对象时，使用 alias（别名）而非 GUID
            - 破坏性操作（clear_page、delete）在 ConfirmRequired 模式下会等待用户确认

            ## 语言
            - 用中文回复用户
            """;
    }
}
