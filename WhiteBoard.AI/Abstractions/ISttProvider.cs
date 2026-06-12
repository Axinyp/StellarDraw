namespace WhiteBoard.AI.Abstractions;

/// <summary>语音转文字（STT）提供商抽象。</summary>
public interface ISttProvider
{
    string ProviderName { get; }
    bool   IsAvailable  { get; }

    /// <summary>识别音频字节流，返回转录文字。</summary>
    Task<SttResult> TranscribeAsync(byte[] audioData, string language = "zh",
        CancellationToken ct = default);
}

public sealed record SttResult(
    string  Text,
    float   Confidence,
    string  Language,
    bool    IsPartial);
