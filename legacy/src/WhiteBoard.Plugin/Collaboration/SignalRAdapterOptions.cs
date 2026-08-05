namespace WhiteBoard.Plugin.Collaboration;

/// <summary>SignalRCollaborationAdapter 的连接配置。</summary>
public sealed record SignalRAdapterOptions(
    string HubUrl,
    string? AccessToken = null
);