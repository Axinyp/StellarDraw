namespace WhiteBoard.Core.Domain;

/// <summary>
/// 当前页面的不可变快照，由 WhiteboardSession.CaptureSnapshotAsync 在执行器线程内构建。
/// AI 工具层（QueryToolCatalog）通过此快照安全读取页面状态，无需持有 PageModel 引用。
/// </summary>
public sealed class PageSnapshot
{
    public string   PageId  { get; init; } = "";
    public long     Version { get; init; }
    public float    Width   { get; init; }
    public float    Height  { get; init; }
    public IReadOnlyList<ObjectSummary> Objects { get; init; } = [];
}

/// <summary>对象摘要：仅含位置/尺寸/类型等轻量字段，不含笔迹点或富内容。</summary>
public sealed record ObjectSummary(
    Guid    Id,
    string  Kind,
    string? Alias,
    float   X,
    float   Y,
    float   Width,
    float   Height,
    int     ZIndex,
    bool    IsLocked,
    string? OwnerId);
