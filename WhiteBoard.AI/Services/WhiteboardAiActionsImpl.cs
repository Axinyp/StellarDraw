using System.Text.Json;
using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;

namespace WhiteBoard.AI.Services;

/// <summary>
/// IWhiteboardAiActions 的默认实现。
/// 持有 WhiteboardSession 引用，通过 Session 的公开 AI 方法操作白板。
/// 维护 alias → objectId 映射，供多步 AI 操作引用。
/// </summary>
public sealed class WhiteboardAiActionsImpl : IWhiteboardAiActions
{
    private readonly WhiteboardSession             _session;
    private readonly Dictionary<string, Guid>      _aliasMap = new();

    public WhiteboardAiActionsImpl(WhiteboardSession session) => _session = session;

    // ── Alias 管理 ────────────────────────────────────────────────────────────

    public IReadOnlyDictionary<string, Guid> GetAliasMap()  => _aliasMap;
    public void RegisterAlias(string alias, Guid objectId)  => _aliasMap[alias] = objectId;
    public void ClearAliases()                              => _aliasMap.Clear();

    private Guid? Resolve(string aliasOrId)
    {
        if (_aliasMap.TryGetValue(aliasOrId, out var id)) return id;
        if (Guid.TryParse(aliasOrId, out var g)) return g;
        return null;
    }

    private IReadOnlyList<Guid> ResolveAll(IReadOnlyList<string> aliasesOrIds) =>
        aliasesOrIds.Select(Resolve).OfType<Guid>().ToList();

    // ── Insert ─────────────────────────────────────────────────────────────────

    public async Task<ToolResult> InsertShapeAsync(ShapeSpec spec, string? alias = null,
        string? groupId = null, CancellationToken ct = default)
    {
        var shapeType = spec.ShapeType.ToLowerInvariant() switch
        {
            "circle"    => ShapeType.Circle,
            "line"      => ShapeType.Line,
            "arrow"     => ShapeType.Arrow,
            "triangle"  => ShapeType.Triangle,
            _           => ShapeType.Rectangle,
        };

        var shape = new ShapeObject(
            shapeType,
            ParseColor(spec.FillColor),
            ParseColor(spec.StrokeColor),
            spec.StrokeWidth,
            spec.IsFilled,
            spec.X, spec.Y, spec.Width, spec.Height);

        await _session.AddShapeAsync(shape, groupId: groupId, ct: ct);

        if (alias is not null) RegisterAlias(alias, shape.Id);

        var aliasDict = alias is not null
            ? (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { [alias] = shape.Id.ToString() }
            : new Dictionary<string, string>();

        return ToolResult.Ok(
            created: [shape.Id.ToString()],
            aliases: aliasDict,
            groupId: groupId,
            pageId:  _session.Current.Id,
            version: _session.Current.Version);
    }

    public async Task<ToolResult> InsertTextAsync(TextSpec spec, string? alias = null,
        string? groupId = null, CancellationToken ct = default)
    {
        var style = new TextStyle(
            spec.FontFamily, spec.FontSize, spec.Bold, spec.Italic,
            ParseColor(spec.Color));

        var text = new TextObject(spec.Content, style,
            spec.X, spec.Y, spec.Width, spec.Height);

        await _session.AddTextObjectAsync(text, groupId: groupId, ct: ct);

        if (alias is not null) RegisterAlias(alias, text.Id);

        var aliasDict = alias is not null
            ? (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { [alias] = text.Id.ToString() }
            : new Dictionary<string, string>();

        return ToolResult.Ok(
            created: [text.Id.ToString()],
            aliases: aliasDict,
            groupId: groupId,
            pageId:  _session.Current.Id,
            version: _session.Current.Version);
    }

    public async Task<ToolResult> InsertImageAsync(ImageSpec spec, string? alias = null,
        string? groupId = null, CancellationToken ct = default)
    {
        var assetId = Guid.NewGuid().ToString("N");
        var image   = new ImageObject(assetId, spec.X, spec.Y, spec.Width, spec.Height);

        await _session.AddShapeAsync(
            new ShapeObject(ShapeType.Rectangle, 0xFFFFFFFF, 0xFF888888, 1f, true,
                spec.X, spec.Y, spec.Width, spec.Height),
            groupId: groupId, ct: ct);

        return ToolResult.Ok(
            created: [image.Id.ToString()],
            pageId:  _session.Current.Id,
            version: _session.Current.Version);
    }

    // ── Edit ──────────────────────────────────────────────────────────────────

    public async Task<ToolResult> MoveObjectsAsync(IReadOnlyList<string> aliasesOrIds,
        float dx, float dy, string? groupId = null, CancellationToken ct = default)
    {
        var ids = ResolveAll(aliasesOrIds);
        if (ids.Count == 0) return ToolResult.Error("No valid objects found");

        await _session.MoveObjectsAsync(ids, dx, dy, groupId, ct);

        return ToolResult.Ok(
            updated: ids.Select(id => id.ToString()).ToList(),
            groupId: groupId,
            pageId:  _session.Current.Id,
            version: _session.Current.Version);
    }

    public async Task<ToolResult> DeleteObjectsAsync(IReadOnlyList<string> aliasesOrIds,
        string? groupId = null, CancellationToken ct = default)
    {
        var ids = ResolveAll(aliasesOrIds);
        if (ids.Count == 0) return ToolResult.Error("No valid objects found");

        await _session.DeleteObjectsAsync(ids, groupId, ct);

        foreach (var alias in aliasesOrIds.Where(a => _aliasMap.ContainsKey(a)))
            _aliasMap.Remove(alias);

        return ToolResult.Ok(
            deleted: ids.Select(id => id.ToString()).ToList(),
            pageId:  _session.Current.Id,
            version: _session.Current.Version);
    }

    public async Task<ToolResult> ClearPageAsync(CancellationToken ct = default)
    {
        await _session.ClearCurrentPageAsync(ct: ct);
        _aliasMap.Clear();
        return ToolResult.Ok(pageId: _session.Current.Id, version: _session.Current.Version);
    }

    public async Task<ToolResult> UndoAsync(string? groupId = null, CancellationToken ct = default)
    {
        await _session.UndoAsync(ct);
        return ToolResult.Ok(pageId: _session.Current.Id, version: _session.Current.Version);
    }

    public async Task<ToolResult> RedoAsync(CancellationToken ct = default)
    {
        await _session.RedoAsync(ct);
        return ToolResult.Ok(pageId: _session.Current.Id, version: _session.Current.Version);
    }

    public async Task<ToolResult> SelectObjectsAsync(float x, float y, float w, float h,
        CancellationToken ct = default)
    {
        await _session.SelectObjectsInRegionAsync(x, y, w, h, ct);
        var selected = _session.Selection.ToList();
        return ToolResult.Ok(
            updated: selected.Select(id => id.ToString()).ToList(),
            pageId:  _session.Current.Id);
    }

    public Task<PageSnapshot> GetSnapshotAsync(CancellationToken ct = default)
        => _session.CaptureSnapshotAsync(ct);

    // ── Navigation ─────────────────────────────────────────────────────────────

    public async Task<ToolResult> AddPageAsync(string? title = null, CancellationToken ct = default)
    {
        string newPageId = "";
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _session.PostOperationAsync(() =>
        {
            var page = _session.Pages.AddPage(title);
            _session.Pages.GoToPage(page.Id);
            newPageId = page.Id;
            done.TrySetResult();
            return ValueTask.CompletedTask;
        }, ct);
        await done.Task.WaitAsync(ct);
        return ToolResult.Ok(pageId: newPageId);
    }

    public async Task<ToolResult> NavigateToPageAsync(int pageIndex, CancellationToken ct = default)
    {
        bool success = false;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _session.PostOperationAsync(() =>
        {
            int count = _session.Pages.AllPages.Count;
            if (pageIndex >= 0 && pageIndex < count)
            {
                _session.Pages.GoToIndex(pageIndex);
                success = true;
            }
            done.TrySetResult();
            return ValueTask.CompletedTask;
        }, ct);
        await done.Task.WaitAsync(ct);
        return success
            ? ToolResult.Ok(pageId: _session.Current.Id, version: _session.Current.Version)
            : ToolResult.Error($"页面索引 {pageIndex} 超出范围（共 {_session.Pages.AllPages.Count} 页）");
    }

    public async Task<string> GetPagesInfoAsync(CancellationToken ct = default)
    {
        string result = "";
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _session.PostOperationAsync(() =>
        {
            var pages = _session.Pages.AllPages;
            result = JsonSerializer.Serialize(new
            {
                count   = pages.Count,
                current = _session.Pages.CurrentIndex,
                pages   = pages.Select((p, i) => new { index = i, id = p.Id, title = p.Title })
            });
            done.TrySetResult();
            return ValueTask.CompletedTask;
        }, ct);
        await done.Task.WaitAsync(ct);
        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static uint ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6) hex = "FF" + hex;
        return uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var v)
            ? v : 0xFF000000;
    }
}
