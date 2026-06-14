using System.Speech.AudioFormat;
using System.Speech.Recognition;
using WhiteBoard.AI.Abstractions;

namespace WhiteBoard.AI.Voice;

/// <summary>
/// 基于 Windows System.Speech（SAPI）的 STT 提供商。
/// 无需额外 NuGet 包（已通过 System.Speech 包引入），识别精度低于 Whisper。
/// 适用场景：离线/内网环境，不要求高精度。
/// </summary>
public sealed class WindowsSpeechSttProvider : ISttProvider, IDisposable
{
    private readonly SpeechRecognitionEngine? _engine;

    public string ProviderName => "windows-speech";

    public bool IsAvailable
    {
        get { try { return _engine is not null; } catch { return false; } }
    }

    public WindowsSpeechSttProvider(string locale = "zh-CN")
    {
        try
        {
            _engine = new SpeechRecognitionEngine(
                new System.Globalization.CultureInfo(locale));
            _engine.LoadGrammar(new DictationGrammar());
            _engine.SetInputToDefaultAudioDevice();
        }
        catch
        {
            _engine = null;
        }
    }

    public async Task<SttResult> TranscribeAsync(byte[] audioData,
        string language = "zh", CancellationToken ct = default)
    {
        if (_engine is null)
            return new SttResult("", 0f, language, false);

        return await Task.Run(() =>
        {
            try
            {
                using var ms = new MemoryStream(audioData);
                var format = new SpeechAudioFormatInfo(
                    16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono);

                _engine.SetInputToAudioStream(ms, format);
                var result = _engine.Recognize();

                return result is null
                    ? new SttResult("", 0f, language, false)
                    : new SttResult(result.Text, result.Confidence, language, false);
            }
            catch
            {
                return new SttResult("", 0f, language, false);
            }
        }, ct);
    }

    public void Dispose() => _engine?.Dispose();
}