using System.Runtime.CompilerServices;
using System.Text.Json;
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
            // ── LLM 调用 ─────────────────────────────────────────────────────
            // yield return cannot appear in a catch block — capture events outside
            ChatResponse?    response   = null;
            AgentLoopEvent?  errorEvent = null;

            try
            {
                response = await _llm.GetResponseAsync(_history.Messages, options, ct);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            catch (Exception ex)
            {
                errorEvent = new AgentErrorEvent(ex.Message, IsRetryable(ex));
            }

            if (errorEvent is not null)
            {
                yield return errorEvent;
                yield break;
            }

            // response is non-null here
            var assistantMsg = response!.Messages.LastOrDefault()
                               ?? new ChatMessage(ChatRole.Assistant, "");
            _history.AddMessage(assistantMsg);

            // ── 没有工具调用 → 输出最终文字并结束 ──────────────────────────
            if (response.FinishReason != ChatFinishReason.ToolCalls)
            {
                var text = response.Text ?? "";
                if (!string.IsNullOrEmpty(text))
                    yield return new TextChunkEvent(text);
                yield break;
            }

            // ── 处理工具调用 ─────────────────────────────────────────────────
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

    private static string SerializeArgs(IDictionary<string, object?>? args)
    {
        if (args is null || args.Count == 0) return "{}";
        try { return JsonSerializer.Serialize(args); }
        catch { return "{}"; }
    }

    private static bool IsRetryable(Exception ex) =>
        ex is HttpRequestException or TimeoutException;
}
