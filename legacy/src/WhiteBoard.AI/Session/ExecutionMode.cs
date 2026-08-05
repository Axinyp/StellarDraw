namespace WhiteBoard.AI.Session;

/// <summary>AI 工具执行模式。</summary>
public enum ExecutionMode
{
    /// <summary>全自动执行，无需用户确认。</summary>
    Auto,

    /// <summary>破坏性操作（clear_page、delete 等）执行前需用户确认。</summary>
    ConfirmRequired,
}