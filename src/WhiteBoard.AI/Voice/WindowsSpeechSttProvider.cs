using System.IO;
using System.Speech.Recognition;
using WhiteBoard.AI.Abstractions;

namespace WhiteBoard.AI.Voice;

/// <summary>
/// 基于 Windows System.Speech（SAPI）的 STT 提供商。
/// 无需额外 NuGet 包（已通过 System.Speech 包引入），识别精度低于 Whisper。
/// 适用场景：离线/内网环境，不要求高精度。
/// 前提：系统需已安装对应语言（如 zh-CN）的「语音识别」语言包，否则引擎初始化失败。
/// </summary>
public sealed class WindowsSpeechSttProvider : ISttProvider, IDisposable
{
    private readonly SpeechRecognitionEngine? _engine;

    /// <summary>初始化失败原因（语言包缺失、麦克风不可用等）；null 表示成功。</summary>
    public string? InitializationError { get; }

    public string ProviderName => "windows-speech";

    public bool IsAvailable => _engine is not null;

    public WindowsSpeechSttProvider(string locale = "zh-CN")
    {
        try
        {
            _engine = new SpeechRecognitionEngine(
                new System.Globalization.CultureInfo(locale));
            _engine.LoadGrammar(new DictationGrammar());
        }
        catch (Exception ex)
        {
            InitializationError =
                $"Windows 语音识别初始化失败（可能未安装 {locale} 语音识别语言包）: {ex.Message}";
            _engine = null;
        }
    }

    public async Task<SttResult> TranscribeAsync(byte[] audioData,
        string language = "zh", CancellationToken ct = default)
    {
        if (_engine is null)
            throw new InvalidOperationException(
                InitializationError ?? "Windows 语音识别引擎不可用");

        return await Task.Run(() =>
        {
            // SAPI 的 SetInputToAudioStream 需要与 SpeechAudioFormatInfo 完全匹配的裸 PCM，
            // 直接喂带 RIFF 头的 WAV 会把头当音频。改用临时 WAV 文件 + SetInputToWaveFile，
            // 由 SAPI 自行解析 WAV 头，避免采样率/位深/声道硬编码不匹配导致识别为空。
            var tmp = Path.Combine(Path.GetTempPath(), $"wb_stt_{Guid.NewGuid():N}.wav");
            try
            {
                File.WriteAllBytes(tmp, audioData);
                _engine.SetInputToWaveFile(tmp);
                var result = _engine.Recognize();

                return result is null
                    ? new SttResult("", 0f, language, false)
                    : new SttResult(result.Text, result.Confidence, language, false);
            }
            finally
            {
                try { File.Delete(tmp); } catch { /* 临时文件清理失败可忽略 */ }
            }
        }, ct);
    }

    public void Dispose() => _engine?.Dispose();
}