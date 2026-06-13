using System.Text.Json;

namespace WhiteBoard.AI.Tests;

/// <summary>
/// AIWhiteboardSettings 测试：序列化往返完整性与默认值校验。
/// </summary>
public class SettingsTests
{
    // ── 默认值 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Default_LlmProvider_IsOllama()
    {
        Assert.Equal("ollama", AIWhiteboardSettings.Default.LlmProviderType);
    }

    [Fact]
    public void Default_ExecutionMode_IsConfirmRequired()
    {
        Assert.Equal("ConfirmRequired", AIWhiteboardSettings.Default.ExecutionMode);
    }

    [Fact]
    public void Default_McpServer_IsDisabled()
    {
        Assert.False(AIWhiteboardSettings.Default.McpServer.Enabled);
    }

    [Fact]
    public void Default_McpClient_IsDisabled()
    {
        Assert.False(AIWhiteboardSettings.Default.McpClient.Enabled);
    }

    // ── 序列化往返 ────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_PreservesAllLlmFields()
    {
        var original = new AIWhiteboardSettings
        {
            LlmProviderType = "openai",
            LlmEndpoint     = "https://api.openai.com/v1",
            LlmModel        = "gpt-4o",
            LlmApiKeyRef    = "OPENAI_API_KEY",
            ExecutionMode   = "AutoApprove",
        };

        var json      = JsonSerializer.Serialize(original);
        var restored  = JsonSerializer.Deserialize<AIWhiteboardSettings>(json)!;

        Assert.Equal(original.LlmProviderType, restored.LlmProviderType);
        Assert.Equal(original.LlmEndpoint,     restored.LlmEndpoint);
        Assert.Equal(original.LlmModel,        restored.LlmModel);
        Assert.Equal(original.LlmApiKeyRef,    restored.LlmApiKeyRef);
        Assert.Equal(original.ExecutionMode,   restored.ExecutionMode);
    }

    [Fact]
    public void RoundTrip_PreservesAllSttFields()
    {
        var original = new AIWhiteboardSettings
        {
            SttProviderType = "azure",
            SttLanguage     = "en",
            SttEnabled      = false,
            SttModelPath    = "/models/ggml-large.bin",
            SttEndpoint     = "https://eastus.stt.speech.microsoft.com",
            VoiceMode       = "wake-word",
        };

        var json     = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<AIWhiteboardSettings>(json)!;

        Assert.Equal(original.SttProviderType, restored.SttProviderType);
        Assert.Equal(original.SttLanguage,     restored.SttLanguage);
        Assert.Equal(original.SttEnabled,      restored.SttEnabled);
        Assert.Equal(original.SttModelPath,    restored.SttModelPath);
        Assert.Equal(original.SttEndpoint,     restored.SttEndpoint);
        Assert.Equal(original.VoiceMode,       restored.VoiceMode);
    }

    [Fact]
    public void RoundTrip_PreservesWakeWordFields()
    {
        var original = new AIWhiteboardSettings
        {
            WakeWordEnabled          = true,
            WakeWord                 = "Hello Board",
            WakeWordProviderType     = "windows",
            WakeWordUtteranceSeconds = 8,
        };

        var json     = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<AIWhiteboardSettings>(json)!;

        Assert.Equal(original.WakeWordEnabled,          restored.WakeWordEnabled);
        Assert.Equal(original.WakeWord,                 restored.WakeWord);
        Assert.Equal(original.WakeWordProviderType,     restored.WakeWordProviderType);
        Assert.Equal(original.WakeWordUtteranceSeconds, restored.WakeWordUtteranceSeconds);
    }

    [Fact]
    public void RoundTrip_PreservesMcpServerFields()
    {
        var original = new AIWhiteboardSettings
        {
            McpServer = new McpServerSettings
            {
                Enabled              = true,
                StdioEnabled         = true,
                HttpEnabled          = true,
                HttpUrl              = "http://127.0.0.1:9999",
                RoutePrefix          = "/mcp/v2",
                TokenLifetimeMinutes = 30,
                AllowDestructiveTools = true,
            }
        };

        var json     = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<AIWhiteboardSettings>(json)!;

        Assert.Equal(original.McpServer.Enabled,              restored.McpServer.Enabled);
        Assert.Equal(original.McpServer.HttpUrl,              restored.McpServer.HttpUrl);
        Assert.Equal(original.McpServer.TokenLifetimeMinutes, restored.McpServer.TokenLifetimeMinutes);
        Assert.Equal(original.McpServer.AllowDestructiveTools, restored.McpServer.AllowDestructiveTools);
    }

    [Fact]
    public void RoundTrip_PreservesMcpClientConnections()
    {
        var original = new AIWhiteboardSettings
        {
            McpClient = new McpClientSettings
            {
                Enabled = true,
                Connections =
                [
                    new McpClientConnectionSettings
                    {
                        DisplayName = "File Server",
                        Transport   = "stdio",
                        Command     = "npx",
                        Arguments   = ["-y", "@modelcontextprotocol/server-filesystem"],
                        TrustLevel  = "trusted",
                    }
                ]
            }
        };

        var json     = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<AIWhiteboardSettings>(json)!;

        Assert.Single(restored.McpClient.Connections);
        Assert.Equal("File Server", restored.McpClient.Connections[0].DisplayName);
        Assert.Equal("npx",         restored.McpClient.Connections[0].Command);
    }

    // ── JSON 属性名称（契约稳定性）────────────────────────────────────────────

    [Fact]
    public void Serialized_UsesExpectedPropertyNames()
    {
        var json = JsonSerializer.Serialize(AIWhiteboardSettings.Default);

        Assert.Contains("\"llmProvider\"",  json);
        Assert.Contains("\"llmEndpoint\"",  json);
        Assert.Contains("\"sttProvider\"",  json);
        Assert.Contains("\"voiceMode\"",    json);
        Assert.Contains("\"mcpServer\"",    json);
        Assert.Contains("\"mcpClient\"",    json);
    }
}
