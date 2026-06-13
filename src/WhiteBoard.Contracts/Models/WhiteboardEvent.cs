namespace WhiteBoard.Contracts.Models;

/// <summary>
/// 白板领域事件基类。所有协同操作（笔画新增、对象删除、变换、翻页）均以事件形式传播。
/// <para>
/// EventId：由发送端生成的唯一 ID（GUID hex），接收端用于去重。<br/>
/// PageId / ObjectId：事件作用目标，可为 GUID 或逻辑 ID。<br/>
/// ClientId：发起客户端的标识，接收端据此过滤自身回显。<br/>
/// OccurredAtUtc：客户端本地时间，仅供日志参考，不用于 LWW 排序。
/// </para>
/// </summary>
public abstract record WhiteboardEvent(
    string         EventId,
    string         PageId,
    string         ObjectId,
    string         ClientId,
    DateTimeOffset OccurredAtUtc);

/// <summary>笔画新增事件。StrokeData 为 <see cref="Core.Services.PageSerializer.SerializeObject"/> 产生的二进制块。</summary>
public record StrokeAddedEvent(
    string EventId, string PageId, string ObjectId, string ClientId,
    DateTimeOffset OccurredAtUtc, byte[] StrokeData)
    : WhiteboardEvent(EventId, PageId, ObjectId, ClientId, OccurredAtUtc);

/// <summary>对象删除事件。接收端按 ObjectId 定位并执行 DeleteObjectCommand。</summary>
public record ObjectDeletedEvent(
    string EventId, string PageId, string ObjectId, string ClientId,
    DateTimeOffset OccurredAtUtc)
    : WhiteboardEvent(EventId, PageId, ObjectId, ClientId, OccurredAtUtc);

/// <summary>对象变换事件。Matrix6 = [M11,M12,M21,M22,OffsetX,OffsetY]（Matrix3x2 行主序）。</summary>
public record ObjectTransformedEvent(
    string EventId, string PageId, string ObjectId, string ClientId,
    DateTimeOffset OccurredAtUtc, float[] Matrix6)
    : WhiteboardEvent(EventId, PageId, ObjectId, ClientId, OccurredAtUtc);

/// <summary>新页面添加事件。PageIndex 为建议索引，接收端若已有该 ID 则忽略。</summary>
public record PageAddedEvent(
    string EventId, string PageId, string ObjectId, string ClientId,
    DateTimeOffset OccurredAtUtc, int PageIndex)
    : WhiteboardEvent(EventId, PageId, ObjectId, ClientId, OccurredAtUtc);

/// <summary>
/// 通用对象新增事件（AI 插入形状/文字/图片等）。
/// ObjectData 为 PageSerializer.SerializeObject 产生的二进制块，与 StrokeAddedEvent 格式一致。
/// OperationId 用于幂等去重；GroupId 将多步 AI 操作关联为一个可整体撤销的组。
/// </summary>
public record ObjectAddedEvent(
    string EventId, string PageId, string ObjectId, string ClientId,
    DateTimeOffset OccurredAtUtc, byte[] ObjectData,
    string OperationId, string? GroupId)
    : WhiteboardEvent(EventId, PageId, ObjectId, ClientId, OccurredAtUtc);

/// <summary>
/// 批量变换事件（移动/旋转/缩放多个对象）。
/// Matrix6 = [M11,M12,M21,M22,OffsetX,OffsetY]（Matrix3x2 行主序，与 ObjectTransformedEvent 格式一致）。
/// </summary>
public record ObjectsTransformedEvent(
    string EventId, string PageId, string ObjectId, string ClientId,
    DateTimeOffset OccurredAtUtc, IReadOnlyList<string> ObjectIds, float[] Matrix6,
    string OperationId)
    : WhiteboardEvent(EventId, PageId, ObjectId, ClientId, OccurredAtUtc);

/// <summary>清空整页事件。接收端删除指定页面的全部对象。</summary>
public record PageClearedEvent(
    string EventId, string PageId, string ObjectId, string ClientId,
    DateTimeOffset OccurredAtUtc, string OperationId)
    : WhiteboardEvent(EventId, PageId, ObjectId, ClientId, OccurredAtUtc);
