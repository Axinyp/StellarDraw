using Microsoft.Extensions.AI;

namespace WhiteBoard.AI.Abstractions;

/// <summary>LLM 提供商抽象。底层通过 Microsoft.Extensions.AI IChatClient 统一调用。</summary>
public interface ILlmProvider
{
    string ProviderName { get; }
    bool   IsAvailable  { get; }
    IChatClient CreateClient();
}
