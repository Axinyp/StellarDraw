using System.Text.Json;
using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;
using WhiteBoard.Core.Services;

namespace WhiteBoard.AI.Services;

/// <summary>
/// <see cref="IWhiteboardAiActions"/> 的默认实现。
/// 仅通过 <see cref="WhiteboardSession"/> 的受控异步 API 操作白板，
/// 并维护 AI 会话私有的 alias → objectId 映射。
/// </summary>
public sealed class WhiteboardAiActionsImpl : IWhiteboardAiActions
{
    private readonly WhiteboardSession _session;
    private readonly Dictionary<string, Guid> _aliasMap = new();

    public WhiteboardAiActionsImpl(WhiteboardSession session) => _session = session;

    // ── Alias 管理 ────────────────────────────────────────────────────────────

    public IReadOnlyDictionary<string, Guid> GetAliasMap() => _aliasMap;

    public void RegisterAlias(string alias, Guid objectId) => _aliasMap[alias] = objectId;

    public void ClearAliases() => _aliasMap.Clear();

    public Task<CanvasSizeSnapshot> GetCanvasSizeAsync(CancellationToken ct = default) =>
        _session.GetCanvasSizeAsync(ct);

    private Guid? Resolve(string aliasOrId)
    {
        if (_aliasMap.TryGetValue(aliasOrId, out var id))
            return id;
        return Guid.TryParse(aliasOrId, out var parsed) ? parsed : null;
    }

    private IReadOnlyList<Guid> ResolveAll(IReadOnlyList<string> aliasesOrIds) =>
        aliasesOrIds.Select(Resolve).OfType<Guid>().Distinct().ToArray();

    // ── Insert ─────────────────────────────────────────────────────────────────

    public async Task<ToolResult> InsertShapeAsync(ShapeSpec spec, string? alias = null,
        string? groupId = null, CancellationToken ct = default)
    {
        // 未知形状类型直接报错，避免静默降级为矩形却返回 ok（AI 据此改用 draw_path 或换类型）。
        if (!ShapeTypeParser.TryParse(spec.ShapeType, out var shapeType))
            return ToolResult.Error(
                $"不支持的 shapeType '{spec.ShapeType}'。支持: {ShapeTypeParser.Supported}；" +
                "复杂/任意轮廓请改用 draw_path，复杂图像请用 generate_and_insert_image。");

        var shape = new ShapeObject(
            shapeType,
            ParseColor(spec.FillColor),
            ParseColor(spec.StrokeColor),
            spec.StrokeWidth,
            spec.IsFilled,
            spec.X, spec.Y, spec.Width, spec.Height);
        var result = await _session.AddShapeAsync(shape, groupId: groupId, ct: ct);

        return CreateInsertResult(result, shape.Id, alias, groupId);
    }

    public async Task<ToolResult> InsertPathAsync(PathSpec spec, string? alias = null,
        string? groupId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(spec.PathData))
            return ToolResult.Error("pathData 不能为空");

        var path = new PathObject(
            spec.PathData,
            ParseColor(spec.FillColor),
            ParseColor(spec.StrokeColor),
            spec.StrokeWidth,
            spec.IsFilled);
        var result = await _session.AddPathAsync(path, groupId: groupId, ct: ct);

        return CreateInsertResult(result, path.Id, alias, groupId);
    }

    public async Task<ToolResult> InsertTextAsync(TextSpec spec, string? alias = null,
        string? groupId = null, CancellationToken ct = default)
    {
        var style = new TextStyle(
            spec.FontFamily, spec.FontSize, spec.Bold, spec.Italic,
            ParseColor(spec.Color));
        var text = new TextObject(spec.Content, style,
            spec.X, spec.Y, spec.Width, spec.Height);
        var result = await _session.AddTextObjectAsync(text, groupId: groupId, ct: ct);

        return CreateInsertResult(result, text.Id, alias, groupId);
    }

    /// <summary>
    /// 插入图片对象。把内联图片字节存入跨层资产仓库（<see cref="RawImageAssetStore"/>），
    /// 生成真实 <see cref="ImageObject"/> 入库，注册 alias 并返回真实对象 Id。
    /// 供 generate_and_insert_image（DALL-E/SD）与数学/LaTeX 渲染工具复用。
    /// </summary>
    public async Task<ToolResult> InsertImageAsync(ImageSpec spec, string? alias = null,
        string? groupId = null, CancellationToken ct = default)
    {
        if (spec.ImageData is null || spec.ImageData.Length == 0)
            return ToolResult.Error("ImageData 为空：当前仅支持内联图片字节，Url 远程拉取尚未实现");

        // 按内容生成 AssetId，并仅在格式和资源预算校验通过后存入跨层仓库。
        var assetId = RawImageAssetStore.ComputeAssetId(spec.ImageData);
        if (!RawImageAssetStore.TryStore(assetId, spec.ImageData))
            return ToolResult.Error("ImageData 格式无效或超出图片资源预算");

        var image = new ImageObject(assetId, spec.X, spec.Y, spec.Width, spec.Height);
        var result = await _session.AddImageAsync(image, groupId: groupId, ct: ct);

        return CreateInsertResult(result, image.Id, alias, groupId);
    }

    // ── Edit ──────────────────────────────────────────────────────────────────

    public async Task<ToolResult> MoveObjectsAsync(IReadOnlyList<string> aliasesOrIds,
        float dx, float dy, string? groupId = null, CancellationToken ct = default)
    {
        var ids = ResolveAll(aliasesOrIds);
        if (ids.Count == 0)
            return ToolResult.Error("未找到有效对象");

        var result = await _session.MoveObjectsAsync(ids, dx, dy, operationId: groupId, ct: ct);
        return CreateResult(result, updated: ToStrings(result.AffectedObjectIds), groupId: groupId);
    }

    public async Task<ToolResult> DeleteObjectsAsync(IReadOnlyList<string> aliasesOrIds,
        string? groupId = null, CancellationToken ct = default)
    {
        var ids = ResolveAll(aliasesOrIds);
        if (ids.Count == 0)
            return ToolResult.Error("未找到有效对象");

        var result = await _session.DeleteObjectsAsync(ids, operationId: groupId, ct: ct);
        if (result.Changed)
        {
            var deleted = result.AffectedObjectIds.ToHashSet();
            foreach (var alias in _aliasMap
                .Where(pair => deleted.Contains(pair.Value))
                .Select(pair => pair.Key)
                .ToArray())
            {
                _aliasMap.Remove(alias);
            }
        }

        return CreateResult(result, deleted: ToStrings(result.AffectedObjectIds), groupId: groupId);
    }

    public async Task<ToolResult> ClearPageAsync(CancellationToken ct = default)
    {
        var result = await _session.ClearCurrentPageAsync(ct: ct);
        if (result.Changed)
            _aliasMap.Clear();
        return CreateResult(result, deleted: ToStrings(result.AffectedObjectIds));
    }

    public async Task<ToolResult> UndoAsync(string? groupId = null, CancellationToken ct = default)
    {
        var result = await _session.UndoAsync(ct);
        return CreateResult(result, groupId: groupId);
    }

    public async Task<ToolResult> RedoAsync(CancellationToken ct = default)
    {
        var result = await _session.RedoAsync(ct);
        return CreateResult(result);
    }

    public async Task<ToolResult> SelectObjectsAsync(float x, float y, float w, float h,
        CancellationToken ct = default)
    {
        var result = await _session.SelectObjectsInRegionAsync(x, y, w, h, ct);
        return CreateResult(result, updated: ToStrings(result.AffectedObjectIds));
    }

    public async Task<PageSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var snapshot = await _session.CaptureSnapshotAsync(ct);
        var aliasesByObjectId = _aliasMap
            .GroupBy(pair => pair.Value)
            .ToDictionary(group => group.Key, group => group.First().Key);
        var objects = snapshot.Objects
            .Select(obj => aliasesByObjectId.TryGetValue(obj.Id, out var alias)
                ? obj with { Alias = alias }
                : obj)
            .ToArray();
        return new PageSnapshot
        {
            PageId = snapshot.PageId,
            Version = snapshot.Version,
            Width = snapshot.Width,
            Height = snapshot.Height,
            Objects = objects,
        };
    }

    // ── Navigation ─────────────────────────────────────────────────────────────

    public async Task<ToolResult> AddPageAsync(string? title = null, CancellationToken ct = default)
    {
        var page = await _session.AddPageAsync(title, ct: ct);
        _aliasMap.Clear();
        return ToolResult.Ok(pageId: page.Id, version: page.Version);
    }

    public async Task<ToolResult> NavigateToPageAsync(int pageIndex, CancellationToken ct = default)
    {
        var result = await _session.NavigateToPageAsync(pageIndex, ct);
        if (!result.Changed)
        {
            var pages = await _session.GetPagesAsync(ct);
            return ToolResult.Error($"页面索引 {pageIndex} 超出范围（共 {pages.Pages.Count} 页）");
        }

        _aliasMap.Clear();
        return CreateResult(result);
    }

    public async Task<string> GetPagesInfoAsync(CancellationToken ct = default)
    {
        var snapshot = await _session.GetPagesAsync(ct);
        return JsonSerializer.Serialize(new
        {
            count = snapshot.Pages.Count,
            current = snapshot.CurrentIndex,
            pages = snapshot.Pages.Select(page => new
            {
                index = page.Index,
                id = page.Id,
                title = page.Title,
            }),
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ToolResult CreateInsertResult(
        SessionOperationResult result,
        Guid objectId,
        string? alias,
        string? groupId)
    {
        IReadOnlyDictionary<string, string>? aliases = null;
        if (result.Changed && alias is not null)
        {
            RegisterAlias(alias, objectId);
            aliases = new Dictionary<string, string> { [alias] = objectId.ToString() };
        }

        return CreateResult(
            result,
            created: result.Changed ? [objectId.ToString()] : [],
            aliases: aliases,
            groupId: groupId);
    }

    private static ToolResult CreateResult(
        SessionOperationResult result,
        IReadOnlyList<string>? created = null,
        IReadOnlyList<string>? updated = null,
        IReadOnlyList<string>? deleted = null,
        IReadOnlyDictionary<string, string>? aliases = null,
        string? groupId = null) =>
        ToolResult.Ok(
            created,
            updated,
            deleted,
            aliases,
            groupId,
            result.PageId,
            result.PageVersion);

    private static IReadOnlyList<string> ToStrings(IEnumerable<Guid> ids) =>
        ids.Select(id => id.ToString()).ToArray();

    private static uint ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
            hex = "FF" + hex;
        return uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value)
            ? value
            : 0xFF000000;
    }
}
