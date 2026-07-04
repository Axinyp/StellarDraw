using System.IO;
using System.Text;
using Whisper.net;
using WhiteBoard.AI.Abstractions;

namespace WhiteBoard.AI.Voice;

/// <summary>
/// 基于 Whisper.net 的本地离线 STT 提供商。
/// 加载本地 ggml 模型（ggml-*.bin）在设备上直接推理，无需联网、无需 API Key，
/// 中文识别精度远高于 Windows 桌面 SAPI。原生推理库由 Whisper.net.Runtime 提供。
/// 输入要求 16kHz/单声道 WAV —— 与 <see cref="VoiceRecorder"/> 的采集格式一致。
/// </summary>
public sealed class LocalWhisperSttProvider : ISttProvider, IDisposable
{
    private readonly WhisperFactory? _factory;

    /// <summary>初始化失败原因（模型文件缺失、加载失败等）；null 表示成功。</summary>
    public string? InitializationError { get; }

    public string ProviderName => "whisper-local";

    public bool IsAvailable => _factory is not null;

    /// <param name="modelPath">ggml 模型文件路径（如 ggml-small.bin / ggml-medium.bin）。</param>
    public LocalWhisperSttProvider(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            InitializationError =
                $"本地 Whisper 模型文件不存在: '{modelPath}'。" +
                "请下载 ggml-*.bin（如 ggml-small.bin，中文推荐 ggml-medium.bin）并在设置中指定路径。";
            return;
        }

        try
        {
            _factory = WhisperFactory.FromPath(modelPath);
        }
        catch (Exception ex)
        {
            InitializationError = $"加载本地 Whisper 模型失败: {ex.Message}";
        }
    }

    public async Task<SttResult> TranscribeAsync(byte[] audioData,
        string language = "zh", CancellationToken ct = default)
    {
        if (_factory is null)
            throw new InvalidOperationException(
                InitializationError ?? "本地 Whisper 引擎不可用");

        // 每次转录新建 processor：WhisperProcessor 非线程安全，且 PTT 调用频率低，开销可接受。
        await using var processor = _factory.CreateBuilder()
            .WithLanguage(string.IsNullOrWhiteSpace(language) ? "auto" : language)
            .Build();

        using var wav = new MemoryStream(audioData);
        var sb = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(wav, ct).ConfigureAwait(false))
            sb.Append(segment.Text);

        return new SttResult(sb.ToString().Trim(), 0.9f, language, false);
    }

    public void Dispose() => _factory?.Dispose();
}
