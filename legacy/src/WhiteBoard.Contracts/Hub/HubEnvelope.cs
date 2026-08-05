namespace WhiteBoard.Contracts.Hub;

/// <summary>
/// SignalR 总线上白板事件的线格式信封。
/// T = 类型鉴别符，P = 具体 WhiteboardEvent 子类型的 JSON 载荷。
/// </summary>
public sealed record HubEnvelope(string T, string P);