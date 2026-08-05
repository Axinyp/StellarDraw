using Microsoft.Extensions.AI;

namespace WhiteBoard.AI.Session;

/// <summary>
/// 对话历史管理：维护滑动窗口 + Token 预算，防止超出模型上下文限制。
/// 裁剪优先级（token 预算耗尽时）：
///   1. 永远保留 system prompt（第一条消息）
///   2. 永远保留别名映射摘要（最后一次工具调用结果摘要）
///   3. 优先裁剪旧的工具结果 JSON → 旧的 AI 散文回复 → 旧的用户消息
/// </summary>
public sealed class ConversationHistory
{
    private readonly List<ChatMessage> _messages = [];
    private readonly int _maxTokenBudget;

    public ConversationHistory(int maxTokenBudget = 32_000) =>
        _maxTokenBudget = maxTokenBudget;

    public IReadOnlyList<ChatMessage> Messages => _messages;
    public int Count => _messages.Count;

    public void AddSystem(string systemPrompt)
    {
        if (_messages.Count == 0 || _messages[0].Role != ChatRole.System)
            _messages.Insert(0, new ChatMessage(ChatRole.System, systemPrompt));
        else
            _messages[0] = new ChatMessage(ChatRole.System, systemPrompt);
    }

    public void AddUser(string content) =>
        _messages.Add(new ChatMessage(ChatRole.User, content));

    public void AddAssistant(string content) =>
        _messages.Add(new ChatMessage(ChatRole.Assistant, content));

    public void AddMessage(ChatMessage msg) =>
        _messages.Add(msg);

    /// <summary>
    /// 裁剪消息列表以适应 token 预算（粗略估算：4 字符 ≈ 1 token）。
    /// 保留 system prompt 和最近 N 轮对话。
    /// </summary>
    public void TrimToTokenBudget()
    {
        int totalTokens = EstimateTokens();
        if (totalTokens <= _maxTokenBudget) return;

        // 保留 system prompt（索引 0），从索引 1 开始裁剪最旧的消息
        while (EstimateTokens() > _maxTokenBudget && _messages.Count > 2)
            _messages.RemoveAt(1);
    }

    public void Clear()
    {
        var system = _messages.Count > 0 && _messages[0].Role == ChatRole.System
            ? _messages[0] : null;
        _messages.Clear();
        if (system is not null) _messages.Add(system);
    }

    private int EstimateTokens() =>
        _messages.Sum(m => m.Contents.OfType<TextContent>()
            .Sum(c => (c.Text?.Length ?? 0) / 4));
}