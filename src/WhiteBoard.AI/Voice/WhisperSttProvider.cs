using WhiteBoard.AI.Abstractions;

namespace WhiteBoard.AI.Voice;

/// <summary>
/// 基于 OpenAI Whisper API 的 STT 提供商（云端/本地）。
/// 发送 WAV 到 /v1/audio/transcriptions，支持 Whisper-1 及兼容 API（如本地 Whisper.cpp）。
/// 与 LlmProviderFactory 共用 OpenAI client 路径，但端点和模型可独立配置。
/// </summary>
public sealed class WhisperSttProvider : ISttProvider, IDisposable
{
    private readonly OpenAI.Audio.AudioClient _client;
    private readonly string _model;

    public string ProviderName => "whisper";
    public bool IsAvailable => true;

    /// <param name="apiKey">OpenAI / 兼容 API 密钥。</param>
    /// <param name="endpoint">端点 URL。null 表示使用 OpenAI 官方 API。</param>
    /// <param name="model">Whisper 模型名称，默认 whisper-1。</param>
    public WhisperSttProvider(string apiKey, Uri? endpoint = null, string model = "whisper-1")
    {
        _model = model;
        var options = endpoint is null
            ? null
            : new OpenAI.OpenAIClientOptions { Endpoint = endpoint };

        var openAiClient = options is null
            ? new OpenAI.OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey))
            : new OpenAI.OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), options);

        _client = openAiClient.GetAudioClient(model);
    }

    public async Task<SttResult> TranscribeAsync(byte[] audioData,
        string language = "zh", CancellationToken ct = default)
    {
        try
        {
            using var stream = new MemoryStream(audioData);

            var transcription = await _client.TranscribeAudioAsync(
                stream, "audio.wav",
                new OpenAI.Audio.AudioTranscriptionOptions
                {
                    Language = language,
                    ResponseFormat = OpenAI.Audio.AudioTranscriptionFormat.Simple,
                },
                ct).ConfigureAwait(false);

            var text = transcription.Value.Text ?? "";
            return new SttResult(text, 0.95f, language, false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new SttResult($"[Whisper error: {ex.Message}]", 0f, language, false);
        }
    }

    public void Dispose()
    { }
}