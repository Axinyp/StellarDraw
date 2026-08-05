namespace WhiteBoard.AI.Session;

/// <summary>AgentLoop 向 UI 发送的实时事件流。</summary>
public abstract record AgentLoopEvent;

/// <summary>AI 正在调用工具。</summary>
public record ToolCallingEvent(
    string ToolName,
    string ArgsJson) : AgentLoopEvent;

/// <summary>工具调用已完成，携带结果。</summary>
public record ToolResultEvent(
    string ToolName,
    bool Success,
    string ResultJson) : AgentLoopEvent;

/// <summary>AI 文字回复的流式文本片段。</summary>
public record TextChunkEvent(string Chunk) : AgentLoopEvent;

/// <summary>AI 将执行破坏性操作，需要用户确认。调用 Confirm() 继续，Cancel() 跳过。</summary>
public record ConfirmRequiredEvent(
    string ToolName,
    string Description,
    Action Confirm,
    Action Cancel) : AgentLoopEvent;

/// <summary>AI 执行出错，携带是否可重试标志。</summary>
public record AgentErrorEvent(
    string Message,
    bool IsRetryable) : AgentLoopEvent;