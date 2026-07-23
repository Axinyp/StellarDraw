namespace WhiteBoard.Core.Domain;

/// <summary>白板画布的成对逻辑尺寸快照。</summary>
public sealed record CanvasSizeSnapshot(float Width, float Height);

/// <summary>供 UI 与业务层展示的页面元数据快照。</summary>
public sealed record PageInfo(string Id, string Title, string Background, int Index, long Version);

/// <summary>页面顺序及当前页的不可变快照。</summary>
public sealed record PageCollectionSnapshot(
    IReadOnlyList<PageInfo> Pages,
    string CurrentPageId,
    int CurrentIndex);

/// <summary>可持久化白板页面的不可变内容快照。</summary>
public sealed record WhiteboardDocumentPageSnapshot(
    string Id,
    string Title,
    string Background,
    byte[] Data);

/// <summary>完整白板文档的不可变快照，包含页面、当前页、画布尺寸和图片资产。</summary>
public sealed record WhiteboardDocumentSnapshot(
    string DocumentId,
    CanvasSizeSnapshot CanvasSize,
    IReadOnlyList<WhiteboardDocumentPageSnapshot> Pages,
    int CurrentPageIndex,
    IReadOnlyDictionary<string, byte[]> Assets);

/// <summary>撤销和重做能力的不可变状态快照。</summary>
public sealed record HistoryStateSnapshot(bool CanUndo, bool CanRedo);

/// <summary>供 UI 反序列化为私有 PageModel 的当前页渲染快照。</summary>
public sealed record CurrentPageRenderSnapshot(
    string PageId,
    string Title,
    string Background,
    long Version,
    byte[] Data);

/// <summary>由单个 Session 执行器操作捕获的一致 UI 状态。</summary>
public sealed record SessionUiSnapshot(
    long Revision,
    CanvasSizeSnapshot CanvasSize,
    PageCollectionSnapshot Pages,
    HistoryStateSnapshot History,
    IReadOnlyList<Guid> SelectedObjectIds,
    CurrentPageRenderSnapshot CurrentPage);

/// <summary>已在执行器内完成的写操作结果。</summary>
public sealed record SessionOperationResult(
    string PageId,
    long PageVersion,
    IReadOnlyList<Guid> AffectedObjectIds,
    bool Changed);
