using Microsoft.Extensions.AI;
using WhiteBoard.AI.Abstractions;
using WhiteBoard.AI.Settings;

namespace WhiteBoard.AI.Providers;

/// <summary>
/// 基于 <see cref="AIWhiteboardSettings"/> 的 ILlmProvider 实现。
/// 将 <see cref="LlmProviderFactory.Create"/> 包装为统一接口，
/// 供 <see cref="Session.WhiteboardAISession"/> 在构造时注入。
/// </summary>
public sealed class SettingsBackedLlmProvider : ILlmProvider
{
    private readonly AIWhiteboardSettings _settings;
    private readonly ISecretStore _secrets;

    public SettingsBackedLlmProvider(AIWhiteboardSettings settings, ISecretStore secrets)
    {
        _settings = settings;
        _secrets = secrets;
    }

    public string ProviderName => _settings.LlmProviderType;

    public bool IsAvailable => true;

    public IChatClient CreateClient() => LlmProviderFactory.Create(_settings, _secrets);
}