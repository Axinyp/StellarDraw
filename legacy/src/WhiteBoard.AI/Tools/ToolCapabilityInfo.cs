namespace WhiteBoard.AI.Tools;

/// <summary>工具能力元数据，供 AgentLoop 判断是否需要确认、条件注册等。</summary>
public sealed record ToolCapabilityInfo(
    bool IsDestructive,
    bool RequiresConfirmation,
    string[] RequiredProviders,
    bool SupportsStreaming,
    bool SafeInCollaborationMode)
{
    public static ToolCapabilityInfo Safe(bool supportsStreaming = false) =>
        new(false, false, [], supportsStreaming, true);

    public static ToolCapabilityInfo Destructive() =>
        new(true, true, [], false, false);

    public static ToolCapabilityInfo Conditional(params string[] requiredProviders) =>
        new(false, false, requiredProviders, false, true);
}