using System.Speech.Synthesis;
using WhiteBoard.AI.Abstractions;

namespace WhiteBoard.AI.Voice;

/// <summary>
/// 基于 Windows System.Speech.Synthesis 的 TTS 提供商。
/// 无需额外包，支持所有已安装的 Windows 语音包（中文、英文等）。
/// 适用场景：AI 回复文字播报（TtsEnabled = true 时使用）。
/// </summary>
public sealed class WindowsTtsProvider : ITtsProvider, IDisposable
{
    private readonly SpeechSynthesizer? _synth;
    private readonly string             _voice;

    public string ProviderName => "windows-tts";

    public bool IsAvailable
    {
        get
        {
            try { return _synth is not null; }
            catch { return false; }
        }
    }

    public WindowsTtsProvider(string voice = "default")
    {
        _voice = voice;
        try
        {
            _synth = new SpeechSynthesizer();
            if (voice != "default")
                _synth.SelectVoice(voice);
        }
        catch
        {
            _synth = null;
        }
    }

    public Task<byte[]> SynthesizeAsync(string text, string voice = "default",
        CancellationToken ct = default)
    {
        if (_synth is null) return Task.FromResult(Array.Empty<byte>());

        return Task.Run(() =>
        {
            using var ms = new MemoryStream();
            _synth.SetOutputToWaveStream(ms);
            _synth.Speak(text);
            _synth.SetOutputToDefaultAudioDevice();
            return ms.ToArray();
        }, ct);
    }

    /// <summary>直接播放（不经过音频引擎，适合简单场景）。</summary>
    public void SpeakDirect(string text) => _synth?.SpeakAsync(text);

    public void Dispose() => _synth?.Dispose();
}
