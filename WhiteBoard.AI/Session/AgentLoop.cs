using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using WhiteBoard.AI.Services;
using WhiteBoard.AI.Tools;

namespace WhiteBoard.AI.Session;

/// <summary>
/// LLM ↔ 工具调用 Agentic 循环。
/// 通过 IAsyncEnumerable 实时向 UI 推送 AgentLoopEvent（状态条、流式文字、确认弹窗）。
/// </summary>
public sealed class AgentLoop
{
    private readonly IChatClient              _llm;
    private readonly WhiteboardToolRegistry   _registry;
    private readonly ConversationHistory      _history;
    private readonly ProgressiveContextBuilder _context;
    private readonly ExecutionMode            _mode;

    public AgentLoop(
        IChatClient               llm,
        WhiteboardToolRegistry    registry,
        ConversationHistory       history,
        ProgressiveContextBuilder context,
        ExecutionMode             mode = ExecutionMode.ConfirmRequired)
    {
        _llm      = llm;
        _registry = registry;
        _history  = history;
        _context  = context;
        _mode     = mode;
    }

    /// <summary>
    /// 处理一轮用户消息，以事件流形式返回整个 agentic 循环过程。
    /// 调用方 (UI) 通过 await foreach 实时消费事件。
    /// </summary>
    public async IAsyncEnumerable<AgentLoopEvent> RunAsync(
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var tools     = _registry.GetAvailableTools();
        var toolNames = tools.Select(t => t.Name).ToList();

        _history.AddSystem(_context.BuildLevel0(_mode, toolNames));
        _history.AddUser(userMessage);
        _history.TrimToTokenBudget();

        var options  = new ChatOptions { Tools = [.. tools] };
        int maxTurns = 20;

        for (int turn = 0; turn < maxTurns && !ct.IsCancellationRequested; turn++)
        {
            // ── 流式 LLM 调用（Channel 模式：生产者写，迭代器读，不受 try/catch 限制）─
            var channel    = Channel.CreateUnbounded<AgentLoopEvent>(
                new UnboundedChannelOptions { SingleWriter = true });
            var allUpdates = new List<ChatResponseUpdate>();

            var producerTask = ProduceStreamAsync(channel.Writer, allUpdates, options, ct);

            // 消费 channel：yield 在 try/catch 外部，合法
            bool errorSeen = false;
            await foreach (var evt in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (evt is AgentErrorEvent) errorSeen = true;
                yield return evt;
            }

            await producerTask.ConfigureAwait(false);

            if (errorSeen || ct.IsCancellationRequested) yield break;
            if (allUpdates.Count == 0) yield break;

            // ── 从流式更新重建 Assistant 消息 ────────────────────────────────────
            var assistantMsg = AssembleAssistantMessage(allUpdates);
            _history.AddMessage(assistantMsg);

            var finishReason = allUpdates
                .LastOrDefault(u => u.FinishReason.HasValue)?.FinishReason;

            // ── 没有工具调用 → 结束 ───────────────────────────────────────────────
            if (finishReason != ChatFinishReason.ToolCalls) yield break;

            // ── 处理工具调用 ─────────────────────────────────────────────────────
            var toolCalls = assistantMsg.Contents.OfType<FunctionCallContent>().ToList();
            if (toolCalls.Count == 0) yield break;

            var results = new List<FunctionResultContent>(toolCalls.Count);

            foreach (var call in toolCalls)
            {
                if (ct.IsCancellationRequested) yield break;

                var argsJson = SerializeArgs(call.Arguments);
                yield return new ToolCallingEvent(call.Name, argsJson);

                var cap = _registry.GetCapability(call.Name);
                if (cap?.IsDestructive == true && _mode == ExecutionMode.ConfirmRequired)
                {
                    var tcs = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);

                    yield return new ConfirmRequiredEvent(
                        call.Name,
                        $"AI 将执行: {call.Name}",
                        () => tcs.TrySetResult(true),
                        () => tcs.TrySetResult(false));

                    bool confirmed;
                    try { confirmed = await tcs.Task.WaitAsync(ct); }
                    catch (OperationCanceledException) { yield break; }

                    if (!confirmed)
                    {
                        results.Add(new FunctionResultContent(call.CallId, "cancelled by user"));
                        yield return new ToolResultEvent(call.Name, false, "cancelled");
                        continue;
                    }
                }

                string resultJson;
                bool   success;
                try
                {
                    var toolResult = await _registry.InvokeAsync(call.Name, call.Arguments, ct);
                    resultJson = ToolResultEnvelope.Normalize(toolResult);
                    success    = true;
                }
                catch (Exception ex)
                {
                    resultJson = JsonSerializer.Serialize(new { error = ex.Message });
                    success    = false;
                }

                results.Add(new FunctionResultContent(call.CallId, resultJson));
                yield return new ToolResultEvent(call.Name, success, resultJson);
            }

            _history.AddMessage(new ChatMessage(ChatRole.Tool,
                [.. results.Cast<AIContent>()]));
            _history.TrimToTokenBudget();
        }
    }

    // ── 私有辅助 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 后台生产者：读取 LLM 流式响应，文字片段实时写入 channel，完整更新追加到 allUpdates。
    /// 所有异常在此捕获并转换为 AgentErrorEvent，确保 channel 始终被 Complete。
    /// </summary>
    private async Task ProduceStreamAsync(
        ChannelWriter<AgentLoopEvent>  writer,
        List<ChatResponseUpdate>       allUpdates,
        ChatOptions                    options,
        CancellationToken              ct)
    {
        try
        {
            await foreach (var update in _llm.GetStreamingResponseAsync(
                _history.Messages, options, ct).ConfigureAwait(false))
            {
                allUpdates.Add(update);
                if (!string.IsNullOrEmpty(update.Text))
                    writer.TryWrite(new TextChunkEvent(update.Text));
            }
        }
        catch (OperationCanceledException) { /* 正常取消，不写错误事件 */ }
        catch (Exception ex)
        {
            writer.TryWrite(new AgentErrorEvent(ex.Message, IsRetryable(ex)));
        }
        finally
        {
            writer.TryComplete();
        }
    }

    /// <summary>
    /// 将流式更新列表重建为单条 Assistant ChatMessage，处理文本拼接与工具调用去重。
    /// </summary>
    private static ChatMessage AssembleAssistantMessage(
        IReadOnlyList<ChatResponseUpdate> updates)
    {
        // 拼接所有文字片段
        var fullText = string.Concat(updates.Select(u => u.Text ?? ""));

        // 收集工具调用（按 CallId 去重，保留最完整版本）
        var fnCalls = new Dictionary<string, FunctionCallContent>(StringComparer.Ordinal);
        var fnOrder = new List<string>();

        foreach (var update in updates)
        {
            foreach (var fc in update.Contents.OfType<FunctionCallContent>())
            {
                var key = fc.CallId ?? $"auto_{fnOrder.Count}";
                if (!fnCalls.TryGetValue(key, out var existing))
                {
                    fnCalls[key] = fc;
                    fnOrder.Add(key);
                }
                else if ((fc.Arguments?.Count ?? 0) > (existing.Arguments?.Count ?? 0))
                {
                    fnCalls[key] = fc; // 保留参数更完整的版本
                }
            }
        }

        var contents = new List<AIContent>();
        if (!string.IsNullOrEmpty(fullText))
            contents.Add(new TextContent(fullText));
        contents.AddRange(fnOrder.Select(k => (AIContent)fnCalls[k]));

        return contents.Count > 0
            ? new ChatMessage(ChatRole.Assistant, contents)
            : new ChatMessage(ChatRole.Assistant, "");
    }

    private static string SerializeArgs(IDictionary<string, object?>? args)
    {
        if (args is null || args.Count == 0) return "{}";
        try { return JsonSerializer.Serialize(args); }
        catch { return "{}"; }
    }

    private static bool IsRetryable(Exception ex) =>
        ex is HttpRequestException or TimeoutException;
}
