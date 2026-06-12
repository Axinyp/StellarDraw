using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;

namespace WhiteBoard.AI.Services;

/// <summary>
/// AI 工具层与 WhiteboardSession 之间的服务层。
/// AgentLoop 通过此接口操作白板，不直接持有 WhiteboardSession 引用。
/// 所有方法均线程安全（内部通过 Session.PostOperationAsync 序列化）。
/// </summary>
public interface IWhiteboardAiActions
{
    Task<ToolResult> InsertShapeAsync(ShapeSpec spec, string? alias = null,
        string? groupId = null, CancellationToken ct = default);

    Task<ToolResult> InsertTextAsync(TextSpec spec, string? alias = null,
        string? groupId = null, CancellationToken ct = default);

    Task<ToolResult> InsertImageAsync(ImageSpec spec, string? alias = null,
        string? groupId = null, CancellationToken ct = default);

    Task<ToolResult> MoveObjectsAsync(IReadOnlyList<string> aliasesOrIds, float dx, float dy,
        string? groupId = null, CancellationToken ct = default);

    Task<ToolResult> DeleteObjectsAsync(IReadOnlyList<string> aliasesOrIds,
        string? groupId = null, CancellationToken ct = default);

    Task<ToolResult> ClearPageAsync(CancellationToken ct = default);

    Task<ToolResult> UndoAsync(string? groupId = null, CancellationToken ct = default);

    Task<ToolResult> RedoAsync(CancellationToken ct = default);

    Task<ToolResult> SelectObjectsAsync(float x, float y, float w, float h,
        CancellationToken ct = default);

    Task<PageSnapshot> GetSnapshotAsync(CancellationToken ct = default);

    // ── Navigation ─────────────────────────────────────────────────────────────

    /// <summary>在白板末尾添加新页面并切换到该页面。</summary>
    Task<ToolResult> AddPageAsync(string? title = null, CancellationToken ct = default);

    /// <summary>切换到指定页面索引（从 0 开始）。</summary>
    Task<ToolResult> NavigateToPageAsync(int pageIndex, CancellationToken ct = default);

    /// <summary>获取白板页面信息：总数、当前索引、各页 ID 列表（序列化 JSON 字符串）。</summary>
    Task<string> GetPagesInfoAsync(CancellationToken ct = default);

    // ── Alias ─────────────────────────────────────────────────────────────────

    /// <summary>获取当前页的所有对象别名映射（alias → objectId）。</summary>
    IReadOnlyDictionary<string, Guid> GetAliasMap();

    /// <summary>注册或更新别名（工具执行后由 AgentLoop 调用）。</summary>
    void RegisterAlias(string alias, Guid objectId);

    /// <summary>清空别名映射（新会话开始或翻页时调用）。</summary>
    void ClearAliases();
}

// ── 参数规格 ──────────────────────────────────────────────────────────────────

public sealed record ShapeSpec(
    string ShapeType,      // rectangle | circle | line | arrow | triangle
    float  X,
    float  Y,
    float  Width,
    float  Height,
    string FillColor   = "#FFFFFF",
    string StrokeColor = "#000000",
    float  StrokeWidth = 2f,
    bool   IsFilled    = true);

public sealed record TextSpec(
    float  X,
    float  Y,
    float  Width,
    float  Height,
    string Content,
    string FontFamily  = "Microsoft YaHei",
    float  FontSize    = 16f,
    string Color       = "#000000",
    bool   Bold        = false,
    bool   Italic      = false);

public sealed record ImageSpec(
    float   X,
    float   Y,
    float   Width,
    float   Height,
    byte[]? ImageData,
    string? Url        = null);
