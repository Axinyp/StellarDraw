using WhiteBoard.AI.Abstractions;

namespace WhiteBoard.AI.Voice;

/// <summary>
/// 语音管道：协调 VoiceRecorder + ISttProvider，
/// 将识别结果通过回调推送给 AgentLoop。
///
/// 模式：
///   Push-to-Talk — StartPtt() / StopPttAsync() 由 UI 按钮驱动
///   Wake-Word    — AttachWakeWordDetector() 注入检测器后自动触发录音
/// </summary>
public sealed class VoicePipeline : IDisposable
{
    private readonly VoiceRecorder _recorder;
    private readonly ISttProvider _stt;
    private readonly string _language;

    private IWakeWordDetector? _wakeWordDetector;
    private CancellationTokenSource? _utteranceCts;
    private TimeSpan _utteranceTimeout = TimeSpan.FromSeconds(5);

    /// <summary>识别完成后的回调。</summary>
    public event EventHandler<SttResult>? TranscriptionReady;

    /// <summary>管道错误回调。</summary>
    public event EventHandler<Exception>? ErrorOccurred;

    /// <summary>唤醒词命中并开始捕获后续话语时触发（供 UI 提示"已唤醒"）。</summary>
    public event EventHandler? WakeWordActivated;

    /// <summary>唤醒后超时未收到命令，自动退出唤醒态时触发（供 UI 清除"已唤醒"提示）。</summary>
    public event EventHandler? WakeWordDeactivated;

    public VoicePipeline(ISttProvider stt, string language = "zh")
    {
        _stt = stt;
        _language = language;
        _recorder = new VoiceRecorder();
    }

    public bool IsRecording => _recorder.IsRecording;

    // ── Push-to-Talk ──────────────────────────────────────────────────────────

    public void StartPtt() => _recorder.StartRecording();

    /// <summary>停止录音，送入 STT 识别，结果通过 TranscriptionReady 回调。</summary>
    public async Task StopPttAsync(CancellationToken ct = default)
    {
        var audioData = await _recorder.StopRecordingAsync(ct).ConfigureAwait(false);
        if (audioData.Length == 0) return;

        await TranscribeAsync(audioData, ct).ConfigureAwait(false);
    }

    /// <summary>直接传入 PCM/WAV 字节流进行识别（供测试或外部调用）。</summary>
    public async Task TranscribeAsync(byte[] audioData, CancellationToken ct = default)
    {
        if (!_stt.IsAvailable)
        {
            ErrorOccurred?.Invoke(this,
                new InvalidOperationException($"STT provider '{_stt.ProviderName}' is not available."));
            return;
        }

        try
        {
            var result = await _stt.TranscribeAsync(audioData, _language, ct)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(result.Text))
                TranscriptionReady?.Invoke(this, result);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
        }
    }

    // ── Wake-Word 模式 ────────────────────────────────────────────────────────

    /// <summary>
    /// 注入唤醒词检测器，启用 Wake-Word 模式。
    /// 检测到唤醒词后自动开始录音，录音持续 <paramref name="utteranceTimeout"/> 后
    /// 自动停止并送入 STT 识别。
    /// </summary>
    public void AttachWakeWordDetector(IWakeWordDetector detector,
        TimeSpan? utteranceTimeout = null)
    {
        DetachWakeWordDetector();
        _wakeWordDetector = detector;
        if (utteranceTimeout.HasValue)
            _utteranceTimeout = utteranceTimeout.Value;

        if (detector is IInlineCommandWakeWordDetector inline)
        {
            // 自带命令识别：唤醒仅用于提示"已唤醒"，命令文本由检测器直接给出，
            // 不再另开录音器（避免唤醒延迟错过命令 + 双麦克风竞争）。
            inline.WakeWordDetected += OnInlineWakeActivated;
            inline.CommandRecognized += OnInlineCommandRecognized;
            inline.WakeWordExpired += OnInlineWakeExpired;
        }
        else
        {
            _wakeWordDetector.WakeWordDetected += OnWakeWordDetected;
        }
    }

    /// <summary>移除唤醒词检测器，停止后台监听。</summary>
    public void DetachWakeWordDetector()
    {
        if (_wakeWordDetector is null) return;
        if (_wakeWordDetector is IInlineCommandWakeWordDetector inline)
        {
            inline.WakeWordDetected -= OnInlineWakeActivated;
            inline.CommandRecognized -= OnInlineCommandRecognized;
            inline.WakeWordExpired -= OnInlineWakeExpired;
        }
        else
        {
            _wakeWordDetector.WakeWordDetected -= OnWakeWordDetected;
        }
        _wakeWordDetector.StopListening();
        _wakeWordDetector = null;
        _utteranceCts?.Cancel();
        _utteranceCts?.Dispose();
        _utteranceCts = null;
    }

    /// <summary>启动后台唤醒词监听（AttachWakeWordDetector 后调用）。</summary>
    public void StartWakeWordListening() => _wakeWordDetector?.StartListening();

    /// <summary>暂停后台唤醒词监听。</summary>
    public void StopWakeWordListening()
    {
        _wakeWordDetector?.StopListening();
        _utteranceCts?.Cancel();
    }

    private void OnWakeWordDetected(object? sender, EventArgs e)
    {
        if (_recorder.IsRecording) return; // PTT 正在录音，忽略唤醒词

        // 取消并释放上一次未完成的话语捕获（连续唤醒时重置计时）
        var prev = _utteranceCts;
        prev?.Cancel();
        _utteranceCts = new CancellationTokenSource();
        prev?.Dispose();

        _ = Task.Run(() => CaptureUtteranceAsync(_utteranceCts.Token));
    }

    // ── 自带命令识别的检测器（VAD+Whisper）─────────────────────────────────────

    /// <summary>唤醒命中（inline 检测器）：仅提示"已唤醒"，命令由检测器随后给出。</summary>
    private void OnInlineWakeActivated(object? sender, EventArgs e)
        => WakeWordActivated?.Invoke(this, EventArgs.Empty);

    /// <summary>inline 检测器识别出的命令文本：直接走识别完成回调，等同一次语音输入。</summary>
    private void OnInlineCommandRecognized(object? sender, string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            TranscriptionReady?.Invoke(this, new SttResult(text, 0.9f, _language, false));
    }

    /// <summary>inline 检测器唤醒后超时未收到命令：通知 UI 退出"已唤醒"提示。</summary>
    private void OnInlineWakeExpired(object? sender, EventArgs e)
        => WakeWordDeactivated?.Invoke(this, EventArgs.Empty);

    private async Task CaptureUtteranceAsync(CancellationToken ct)
    {
        // 在启动录音前检查是否已被取消（快速连续唤醒时的竞态保护）
        if (ct.IsCancellationRequested) return;

        _recorder.StartRecording();
        WakeWordActivated?.Invoke(this, EventArgs.Empty); // 通知 UI：已唤醒，开始聆听
        try
        {
            // 等待话语超时（用户说完句子的最大时长）
            await Task.Delay(_utteranceTimeout, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 被新的唤醒词或 StopWakeWordListening 取消 → 停止当前录音，不送识别
            await _recorder.StopRecordingAsync(CancellationToken.None).ConfigureAwait(false);
            return;
        }

        var audioData = await _recorder.StopRecordingAsync(CancellationToken.None)
            .ConfigureAwait(false);

        if (audioData.Length > 0)
            await TranscribeAsync(audioData, CancellationToken.None).ConfigureAwait(false);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        DetachWakeWordDetector(); // 已含 _utteranceCts Cancel+Dispose
        _recorder.Dispose();
    }
}