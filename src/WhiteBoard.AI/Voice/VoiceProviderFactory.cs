using WhiteBoard.AI.Abstractions;
using WhiteBoard.AI.Settings;

namespace WhiteBoard.AI.Voice;

/// <summary>
/// 根据 AIWhiteboardSettings 构建 STT / TTS / WakeWord Provider 实例。
/// </summary>
public static class VoiceProviderFactory
{
    public static ISttProvider CreateStt(AIWhiteboardSettings s, ISecretStore secrets)
    {
        return s.SttProviderType.ToLowerInvariant() switch
        {
            "whisper-local" => new LocalWhisperSttProvider(s.SttModelPath),

            "windows" => new WindowsSpeechSttProvider(
                s.SttLanguage == "zh" ? "zh-CN" : s.SttLanguage),

            "whisper" => CreateWhisper(s, secrets),

            var unknown => throw new NotSupportedException(
                $"Unknown STT provider: '{unknown}'. Supported: whisper-local, windows, whisper")
        };
    }

    public static ITtsProvider CreateTts(AIWhiteboardSettings s)
    {
        return s.TtsProviderType.ToLowerInvariant() switch
        {
            "windows" => new WindowsTtsProvider(),
            var unknown => throw new NotSupportedException(
                $"Unknown TTS provider: '{unknown}'. Supported: windows")
        };
    }

    /// <summary>
    /// 创建唤醒词检测器。当前仅支持 "windows" 实现；
    /// 若不支持或配置禁用则返回 null。
    /// </summary>
    public static IWakeWordDetector? CreateWakeWordDetector(AIWhiteboardSettings s)
    {
        if (!s.WakeWordEnabled) return null;

        return s.WakeWordProviderType.ToLowerInvariant() switch
        {
            "windows" => new WindowsSpeechWakeWordDetector(
                wakeWord: s.WakeWord,
                locale: s.SttLanguage == "zh" ? "zh-CN" : s.SttLanguage),

            _ => null // 未知 provider → 静默忽略，不抛出异常
        };
    }

    private static WhisperSttProvider CreateWhisper(AIWhiteboardSettings s, ISecretStore secrets)
    {
        var apiKey = string.IsNullOrWhiteSpace(s.LlmApiKeyRef)
            ? "" : secrets.Load(s.LlmApiKeyRef);

        Uri? endpoint = string.IsNullOrWhiteSpace(s.LlmEndpoint)
            ? null
            : new Uri(s.LlmEndpoint.TrimEnd('/') + "/v1");

        return new WhisperSttProvider(apiKey ?? "", endpoint);
    }
}