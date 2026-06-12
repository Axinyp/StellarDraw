namespace WhiteBoard.AI.Abstractions;

/// <summary>文字转语音（TTS）提供商抽象（可选，用于 AI 语音回复）。</summary>
public interface ITtsProvider
{
    string ProviderName { get; }
    bool   IsAvailable  { get; }

    /// <summary>将文字合成为 PCM/WAV 音频字节流。</summary>
    Task<byte[]> SynthesizeAsync(string text, string voice = "default",
        CancellationToken ct = default);
}
