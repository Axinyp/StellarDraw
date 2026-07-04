using System.Text.Json.Serialization;

namespace WhiteBoard.AI.Settings;

/// <summary>
/// AI 白板所有设置的可序列化模型。
/// API Key 不存入此类（通过 ISecretStore 单独管理）；此处仅存 KeyRef（凭据管理器中的键名）。
/// </summary>
public sealed class AIWhiteboardSettings
{
    // ── LLM ──────────────────────────────────────────────────────────────────

    [JsonPropertyName("llmProvider")]
    public string LlmProviderType { get; set; } = "ollama";

    [JsonPropertyName("llmEndpoint")]
    public string LlmEndpoint { get; set; } = "http://localhost:11434";

    [JsonPropertyName("llmModel")]
    public string LlmModel { get; set; } = "qwen2.5";

    /// <summary>Windows Credential Manager 中的键名，非 API Key 本身。</summary>
    [JsonPropertyName("llmApiKeyRef")]
    public string LlmApiKeyRef { get; set; } = "";

    // ── 执行模式 ───────────────────────────────────────────────────────────────

    [JsonPropertyName("executionMode")]
    public string ExecutionMode { get; set; } = "ConfirmRequired";

    // ── STT ────────────────────────────────────────────────────────────────────

    [JsonPropertyName("sttProvider")]
    public string SttProviderType { get; set; } = "whisper-local";

    [JsonPropertyName("sttLanguage")]
    public string SttLanguage { get; set; } = "zh";

    [JsonPropertyName("sttEnabled")]
    public bool SttEnabled { get; set; } = true;

    /// <summary>Whisper.net 模型文件路径（ggml-*.bin），留空时使用内置 tiny 模型。</summary>
    [JsonPropertyName("sttModelPath")]
    public string SttModelPath { get; set; } = "";

    /// <summary>自定义 STT 接入点 URL（provider = azure 或 custom 时有效）。</summary>
    [JsonPropertyName("sttEndpoint")]
    public string SttEndpoint { get; set; } = "";

    [JsonPropertyName("voiceMode")]
    public string VoiceMode { get; set; } = "push-to-talk";  // push-to-talk | wake-word

    // ── 唤醒词 ─────────────────────────────────────────────────────────────────

    [JsonPropertyName("wakeWordEnabled")]
    public bool WakeWordEnabled { get; set; } = false;

    [JsonPropertyName("wakeWord")]
    public string WakeWord { get; set; } = "嘿白板";

    [JsonPropertyName("wakeWordProvider")]
    public string WakeWordProviderType { get; set; } = "windows";

    /// <summary>唤醒词触发后的最大录音时长（秒）。</summary>
    [JsonPropertyName("wakeWordUtteranceSeconds")]
    public int WakeWordUtteranceSeconds { get; set; } = 5;

    // ── TTS ────────────────────────────────────────────────────────────────────

    [JsonPropertyName("ttsEnabled")]
    public bool TtsEnabled { get; set; } = false;

    [JsonPropertyName("ttsProvider")]
    public string TtsProviderType { get; set; } = "windows";

    // ── 图像生成 ───────────────────────────────────────────────────────────────

    [JsonPropertyName("imageGenEnabled")]
    public bool ImageGenEnabled { get; set; } = false;

    [JsonPropertyName("imageGenProvider")]
    public string ImageGenProviderType { get; set; } = "dalle3";

    [JsonPropertyName("imageGenEndpoint")]
    public string ImageGenEndpoint { get; set; } = "";

    [JsonPropertyName("imageGenApiKeyRef")]
    public string ImageGenApiKeyRef { get; set; } = "";

    // ── UI 显示 ────────────────────────────────────────────────────────────────

    [JsonPropertyName("showToolCallStatusBar")]
    public bool ShowToolCallStatusBar { get; set; } = true;

    // ── MCP 服务端 ────────────────────────────────────────────────────────────

    [JsonPropertyName("mcpServer")]
    public McpServerSettings McpServer { get; set; } = new();

    // ── MCP 客户端 ────────────────────────────────────────────────────────────

    [JsonPropertyName("mcpClient")]
    public McpClientSettings McpClient { get; set; } = new();

    // ── 工厂方法 ───────────────────────────────────────────────────────────────

    public static AIWhiteboardSettings Default => new();
}

public sealed class McpServerSettings
{
    public bool Enabled { get; set; } = false;
    public bool StdioEnabled { get; set; } = true;
    public bool HttpEnabled { get; set; } = true;
    public bool EnableLegacySse { get; set; } = false;
    public string HttpUrl { get; set; } = "http://127.0.0.1:31888";
    public string RoutePrefix { get; set; } = "/mcp";
    public int TokenLifetimeMinutes { get; set; } = 60;
    public bool AllowDestructiveTools { get; set; } = false;
    public List<string> AllowedTransports { get; set; } = ["stdio", "http"];
}

public sealed class McpClientSettings
{
    public bool Enabled { get; set; } = false;
    public List<McpClientConnectionSettings> Connections { get; set; } = [];
}

public sealed class McpClientConnectionSettings
{
    public string ConnectionId { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Transport { get; set; } = "stdio";
    public string Command { get; set; } = "";
    public List<string> Arguments { get; set; } = [];
    public string WorkingDirectory { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string ApiKeyRef { get; set; } = "";
    public string ToolNamePrefix { get; set; } = "ext";
    public string TrustLevel { get; set; } = "restricted";
    public bool AllowDestructiveTools { get; set; } = false;
}