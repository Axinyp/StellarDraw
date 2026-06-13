using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using WhiteBoard.AI.Settings;

namespace WhiteBoard.AI.Providers;

/// <summary>
/// 根据 AIWhiteboardSettings 构建对应的 IChatClient。
/// 支持 ollama / openai / anthropic / azure / custom 五种 Provider。
/// Ollama 通过 OpenAI-compatible 接口（/v1/chat/completions）接入，无需单独包。
/// API Key 从 ISecretStore 读取（Windows Credential Manager），不经过内存明文。
/// </summary>
public static class LlmProviderFactory
{
    /// <summary>
    /// 创建 IChatClient。调用方负责 Dispose。
    /// 如果 Provider 类型未知，抛出 NotSupportedException。
    /// </summary>
    public static IChatClient Create(AIWhiteboardSettings settings, ISecretStore secrets)
    {
        return settings.LlmProviderType.ToLowerInvariant() switch
        {
            "ollama"    => CreateOllamaCompatible(settings),
            "openai"    => CreateOpenAI(settings, secrets),
            "anthropic" => CreateAnthropic(settings, secrets),
            "azure"     => CreateAzureOpenAI(settings, secrets),
            "custom"    => CreateCustomEndpoint(settings, secrets),
            var unknown => throw new NotSupportedException(
                $"Unknown LLM provider: '{unknown}'. " +
                "Supported: ollama, openai, anthropic, azure, custom")
        };
    }

    // Ollama supports OpenAI-compatible /v1 endpoint — no separate package needed.
    private static IChatClient CreateOllamaCompatible(AIWhiteboardSettings s)
    {
        var baseUrl  = string.IsNullOrWhiteSpace(s.LlmEndpoint)
            ? "http://localhost:11434"
            : s.LlmEndpoint.TrimEnd('/');
        var endpoint = new Uri($"{baseUrl}/v1");

        return BuildChatClient("ollama", s.LlmModel, endpoint);
    }

    private static IChatClient CreateOpenAI(AIWhiteboardSettings s, ISecretStore secrets)
    {
        var apiKey = LoadKey(s.LlmApiKeyRef, secrets, "OpenAI");

        if (!string.IsNullOrWhiteSpace(s.LlmEndpoint))
            return BuildChatClient(apiKey, s.LlmModel, new Uri(s.LlmEndpoint));

        return new ChatClient(s.LlmModel, new ApiKeyCredential(apiKey))
            .AsIChatClient();
    }

    private static IChatClient CreateAnthropic(AIWhiteboardSettings s, ISecretStore secrets)
    {
        var apiKey   = LoadKey(s.LlmApiKeyRef, secrets, "Anthropic");
        var endpoint = string.IsNullOrWhiteSpace(s.LlmEndpoint)
            ? "https://api.anthropic.com/v1"
            : s.LlmEndpoint;
        return BuildChatClient(apiKey, s.LlmModel, new Uri(endpoint));
    }

    private static IChatClient CreateAzureOpenAI(AIWhiteboardSettings s, ISecretStore secrets)
    {
        if (string.IsNullOrWhiteSpace(s.LlmEndpoint))
            throw new InvalidOperationException("Azure OpenAI endpoint URL is required.");
        var apiKey = LoadKey(s.LlmApiKeyRef, secrets, "Azure OpenAI");
        return BuildChatClient(apiKey, s.LlmModel, new Uri(s.LlmEndpoint));
    }

    private static IChatClient CreateCustomEndpoint(AIWhiteboardSettings s, ISecretStore secrets)
    {
        if (string.IsNullOrWhiteSpace(s.LlmEndpoint))
            throw new InvalidOperationException("Custom endpoint URL is required.");
        var apiKey = string.IsNullOrWhiteSpace(s.LlmApiKeyRef)
            ? "none"
            : (secrets.Load(s.LlmApiKeyRef) is { Length: > 0 } k ? k : "none");
        return BuildChatClient(apiKey, s.LlmModel, new Uri(s.LlmEndpoint));
    }

    private static IChatClient BuildChatClient(string apiKey, string model, Uri endpoint)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("Model name cannot be empty.");
        var key = string.IsNullOrWhiteSpace(apiKey) ? "none" : apiKey;
        var options = new OpenAIClientOptions { Endpoint = endpoint };
        return new OpenAIClient(new ApiKeyCredential(key), options)
            .GetChatClient(model)
            .AsIChatClient();
    }

    private static string LoadKey(string keyRef, ISecretStore secrets, string providerName)
    {
        if (string.IsNullOrWhiteSpace(keyRef))
            throw new InvalidOperationException(
                $"{providerName} API Key ref is not configured. " +
                "Set llmApiKeyRef in settings and store the key via WindowsCredentialStore.");

        var key = secrets.Load(keyRef);
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                $"{providerName} API Key not found in Windows Credential Manager. " +
                $"Key ref: '{keyRef}'");

        return key;
    }
}
