using WhiteBoard.AI.Abstractions;
using WhiteBoard.AI.Services;
using WhiteBoard.AI.Tools;
using WhiteBoard.AI.Voice;
using WhiteBoard.Core.Domain;

namespace WhiteBoard.AI.Session;

/// <summary>
/// AI 白板会话对外主入口。
/// 组合 AgentLoop、WhiteboardToolRegistry、ConversationHistory、VoicePipeline 等组件，
/// 对上层（WhiteBoard.UI）暴露简洁的 SendMessageAsync / PTT API。
/// </summary>
public sealed class WhiteboardAISession : IAsyncDisposable
{
    private readonly AgentLoop _loop;
    private readonly ConversationHistory _history;
    private readonly WhiteboardToolRegistry _registry;
    private readonly IWhiteboardAiActions _actions;
    private VoicePipeline? _voice;
    private CancellationTokenSource? _cts;
    private IMcpTokenFactory? _mcpTokenFactory;
    private string? _currentMcpToken;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);

    public ExecutionMode ExecutionMode { get; set; }

    /// <summary>语音识别完成并提交文字后触发（供 UI 展示语音泡）。</summary>
    public event EventHandler<string>? VoiceTextReady;

    /// <summary>语音管道出错时触发（STT 不可用、转录失败等），供 UI 提示用户。</summary>
    public event EventHandler<string>? VoiceError;

    /// <summary>MCP Token 签发后触发（供 UI 展示 Token 和配置片段）。</summary>
    public event EventHandler<string>? McpTokenIssued;

    public WhiteboardAISession(
        ILlmProvider llmProvider,
        WhiteboardSession whiteboardSession,
        IWhiteboardAiActions? actions = null,
        IImageGenProvider? imageGen = null,
        IMathRenderer? mathRenderer = null,
        ExecutionMode mode = ExecutionMode.ConfirmRequired)
    {
        ExecutionMode = mode;
        _actions = actions ?? new WhiteboardAiActionsImpl(whiteboardSession);
        _history = new ConversationHistory();
        _registry = new WhiteboardToolRegistry(_actions, imageGen, mathRenderer);
        var context = new ProgressiveContextBuilder(whiteboardSession);
        var client = llmProvider.CreateClient();
        _loop = new AgentLoop(client, _registry, _history, context, mode);
    }

    // ── External Tool Registration (called by MCP integration layer) ──────────

    /// <summary>
    /// Registers pre-resolved external MCP tool functions into the registry.
    /// Called by the application bootstrap after McpClientProvider.GetToolRegistrationsAsync().
    /// </summary>
    public void RegisterExternalTools(
        IReadOnlyList<(Microsoft.Extensions.AI.AIFunction function,
                       bool requiresConfirmation,
                       bool isDestructive)> tools)
        => _registry.RegisterExternalTools(tools);

    /// <summary>Raises McpTokenIssued to notify UI of a newly generated token.</summary>
    public void NotifyMcpTokenIssued(string token)
        => McpTokenIssued?.Invoke(this, token);

    /// <summary>
    /// 把当前 MCP Token 从本会话「摘下」并返回，同时置空本会话对它的引用，
    /// 使随后的 <see cref="DisposeAsync"/> 不会撤销它。用于设置保存重建会话时移交 Token。
    /// 仅应在 UI 线程重建流程中调用（此刻无并发签发）。
    /// </summary>
    public string? DetachMcpTokenForHandoff()
    {
        var token = _currentMcpToken;
        _currentMcpToken = null;
        return token;
    }

    /// <summary>领养一个由旧会话移交来的既有 MCP Token，使本会话成为其属主（负责后续撤销）。</summary>
    public void AdoptMcpToken(string token) => _currentMcpToken = token;

    // ── MCP Token Lifecycle ───────────────────────────────────────────────────

    /// <summary>
    /// 注入 MCP Token 工厂（由应用层在构造后调用，解耦 AI ↔ Mcp.Core 循环依赖）。
    /// </summary>
    public void SetMcpTokenFactory(IMcpTokenFactory factory)
        => _mcpTokenFactory = factory;

    /// <summary>
    /// 通过已注入的工厂签发新 MCP Token，并触发 McpTokenIssued 事件。
    /// 若已有 Token，先撤销旧 Token。若未注入工厂，返回 null。
    /// 使用 SemaphoreSlim 保证并发安全。
    /// </summary>
    public async Task<string?> IssueMcpTokenAsync(
        TimeSpan? lifetime = null,
        bool allowDestructive = false,
        CancellationToken ct = default)
    {
        if (_mcpTokenFactory is null) return null;

        await _tokenGate.WaitAsync(ct);
        try
        {
            if (_currentMcpToken is not null)
            {
                await _mcpTokenFactory.RevokeTokenAsync(_currentMcpToken, ct);
                _currentMcpToken = null;
            }

            var token = await _mcpTokenFactory.IssueTokenAsync(lifetime, allowDestructive, null, ct);
            _currentMcpToken = token;
            McpTokenIssued?.Invoke(this, token);
            return token;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    /// <summary>撤销当前 MCP Token。</summary>
    public async Task RevokeMcpTokenAsync(CancellationToken ct = default)
    {
        if (_mcpTokenFactory is null) return;

        await _tokenGate.WaitAsync(ct);
        try
        {
            if (_currentMcpToken is null) return;
            await _mcpTokenFactory.RevokeTokenAsync(_currentMcpToken, ct);
            _currentMcpToken = null;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    // ── Voice Pipeline ────────────────────────────────────────────────────────

    /// <summary>
    /// 挂载语音管道。
    /// 若传入 <paramref name="wakeWordDetector"/>，自动切换为 Wake-Word 模式并开始后台监听。
    /// </summary>
    public void AttachVoice(ISttProvider stt, string language = "zh",
        IWakeWordDetector? wakeWordDetector = null,
        TimeSpan? utteranceTimeout = null)
    {
        DetachVoice();
        _voice = new VoicePipeline(stt, language);
        _voice.TranscriptionReady += OnTranscriptionReady;
        _voice.ErrorOccurred += OnVoiceError;

        if (wakeWordDetector is not null)
        {
            _voice.AttachWakeWordDetector(wakeWordDetector, utteranceTimeout);
            _voice.StartWakeWordListening();
        }
    }

    public void DetachVoice()
    {
        if (_voice is null) return;
        _voice.StopWakeWordListening();
        _voice.TranscriptionReady -= OnTranscriptionReady;
        _voice.ErrorOccurred -= OnVoiceError;
        _voice.Dispose();
        _voice = null;
    }

    private void OnVoiceError(object? sender, Exception ex)
        => VoiceError?.Invoke(this, ex.Message);

    /// <summary>开始 Push-to-Talk 录音。</summary>
    public void StartPtt() => _voice?.StartPtt();

    /// <summary>结束录音，转录完成后自动触发 SendMessageAsync。</summary>
    public Task StopPttAsync(CancellationToken ct = default)
        => _voice?.StopPttAsync(ct) ?? Task.CompletedTask;

    public bool IsRecording => _voice?.IsRecording ?? false;

    private void OnTranscriptionReady(object? sender, SttResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Text)) return;
        // 仅把识别文字抛给 UI，由 UI（AiChatViewModel）走与打字一致的 SendTextAsync 管道，
        // 以便流式回复、工具调用状态等 AgentLoopEvent 能正常渲染。
        // 不在此处自行 SendMessageAsync：那样事件会被丢弃，导致界面无任何状态响应。
        VoiceTextReady?.Invoke(this, result.Text);
    }

    // ── Text Interface ────────────────────────────────────────────────────────

    public IAsyncEnumerable<AgentLoopEvent> SendMessageAsync(
        string message, CancellationToken ct = default)
    {
        var prev = _cts;
        prev?.Cancel();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        prev?.Dispose();
        return _loop.RunAsync(message, _cts.Token);
    }

    public void Interrupt() => _cts?.Cancel();

    public void ClearHistory()
    {
        _history.Clear();
        _actions.ClearAliases();
    }

    public WhiteboardToolRegistry Registry => _registry;

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        DetachVoice();

        await _tokenGate.WaitAsync();
        try
        {
            if (_currentMcpToken is not null && _mcpTokenFactory is not null)
                await _mcpTokenFactory.RevokeTokenAsync(_currentMcpToken).ConfigureAwait(false);
        }
        finally
        {
            _tokenGate.Release();
            _tokenGate.Dispose();
        }
    }
}