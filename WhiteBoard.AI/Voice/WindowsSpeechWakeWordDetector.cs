using System.Globalization;
using System.Speech.Recognition;
using WhiteBoard.AI.Abstractions;

namespace WhiteBoard.AI.Voice;

/// <summary>
/// 基于 Windows System.Speech 的唤醒词检测器。
/// 在后台以 RecognizeMode.Multiple 持续监听，仅识别预定义短语，CPU 占用极低。
/// 无需额外 NuGet 包（已通过 System.Speech 引入）。
///
/// 局限：识别精度依赖 Windows 语音识别引擎，中文需系统已安装中文语音包。
/// </summary>
public sealed class WindowsSpeechWakeWordDetector : IWakeWordDetector
{
    private readonly SpeechRecognitionEngine? _engine;

    public string WakeWord    { get; }
    public bool   IsListening { get; private set; }
    /// <summary>初始化失败原因（语言包缺失、麦克风不可用等）；null 表示初始化成功。</summary>
    public string? InitializationError { get; private set; }

    public event EventHandler? WakeWordDetected;

    /// <param name="wakeWord">唤醒词，例如 "嘿白板" 或 "hey whiteboard"。</param>
    /// <param name="locale">识别语言区域，例如 "zh-CN"、"en-US"。</param>
    public WindowsSpeechWakeWordDetector(string wakeWord = "嘿白板", string locale = "zh-CN")
    {
        WakeWord = wakeWord;

        try
        {
            _engine = new SpeechRecognitionEngine(new CultureInfo(locale));

            // 严格关键词语法（比 DictationGrammar 轻量，只识别指定短语）
            var gb = new GrammarBuilder(new Choices(wakeWord));
            _engine.LoadGrammar(new Grammar(gb) { Name = "WakeWord" });
            _engine.SetInputToDefaultAudioDevice();
            _engine.SpeechRecognized += OnSpeechRecognized;
        }
        catch (Exception ex)
        {
            // 语音引擎不可用（未安装语言包等）→ IsAvailable 返回 false，StartListening 静默跳过
            InitializationError = ex.Message;
            System.Diagnostics.Debug.WriteLine($"[WakeWordDetector] Init failed: {ex.Message}");
            _engine = null;
        }
    }

    public bool IsAvailable => _engine is not null;

    public void StartListening()
    {
        if (_engine is null || IsListening) return;
        _engine.RecognizeAsync(RecognizeMode.Multiple);
        IsListening = true;
    }

    public void StopListening()
    {
        if (_engine is null || !IsListening) return;
        try { _engine.RecognizeAsyncStop(); } catch { /* 引擎已释放 */ }
        IsListening = false;
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (e.Result.Confidence < 0.6f) return; // 过滤低置信度误触发
        if (e.Result.Text.Contains(WakeWord, StringComparison.OrdinalIgnoreCase))
            WakeWordDetected?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        StopListening();
        _engine?.Dispose();
    }
}
