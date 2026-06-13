using WhiteBoard.AI.Abstractions;
using WhiteBoard.AI.Settings;

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
    private readonly ISttProvider  _stt;
    private readonly string        _language;

    private IWakeWordDetector?       _wakeWordDetector;
    private CancellationTokenSource? _utteranceCts;
    private TimeSpan                 _utteranceTimeout = TimeSpan.FromSeconds(5);

    /// <summary>识别完成后的回调。</summary>
    public event EventHandler<SttResult>?  TranscriptionReady;
    /// <summary>管道错误回调。</summary>
    public event EventHandler<Exception>?  ErrorOccurred;

    public VoicePipeline(ISttProvider stt, string language = "zh")
    {
        _stt      = stt;
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
        _wakeWordDetector.WakeWordDetected += OnWakeWordDetected;
    }

    /// <summary>移除唤醒词检测器，停止后台监听。</summary>
    public void DetachWakeWordDetector()
    {
        if (_wakeWordDetector is null) return;
        _wakeWordDetector.WakeWordDetected -= OnWakeWordDetected;
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

    private async Task CaptureUtteranceAsync(CancellationToken ct)
    {
        // 在启动录音前检查是否已被取消（快速连续唤醒时的竞态保护）
        if (ct.IsCancellationRequested) return;

        _recorder.StartRecording();
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
