using System.Text.Json;
using Microsoft.Extensions.AI;
using WhiteBoard.AI.Services;

namespace WhiteBoard.AI.Tools.Catalogs;

/// <summary>查询工具集：获取画布状态，不修改白板内容。</summary>
public static class QueryToolCatalog
{
    public static void Register(WhiteboardToolRegistry registry, IWhiteboardAiActions actions)
    {
        registry.Register(
            AIFunctionFactory.Create(
                () =>
                {
                    var (w, h) = actions.GetCanvasSize();
                    return JsonSerializer.Serialize(new { width = w, height = h });
                },
                "get_canvas_size",
                "获取画布当前尺寸（宽×高，单位像素）。坐标传参前应先调用此工具确认边界。"),
            ToolCapabilityInfo.Safe());

        registry.Register(
            AIFunctionFactory.Create(
                async (CancellationToken ct = default) =>
                {
                    var snap = await actions.GetSnapshotAsync(ct);
                    return JsonSerializer.Serialize(snap.Objects.Select(o => new
                    {
                        id     = o.Id.ToString(),
                        kind   = o.Kind,
                        alias  = o.Alias,
                        x      = o.X,
                        y      = o.Y,
                        width  = o.Width,
                        height = o.Height,
                        zIndex = o.ZIndex,
                        locked = o.IsLocked,
                    }));
                },
                "list_objects",
                "列出当前页面所有对象的摘要（ID、类型、别名、位置、尺寸）。" +
                "数量较多时建议先调用此工具了解布局再操作。"),
            ToolCapabilityInfo.Safe());

        registry.Register(
            AIFunctionFactory.Create(
                async (string aliasOrId, CancellationToken ct = default) =>
                {
                    var snap = await actions.GetSnapshotAsync(ct);
                    var obj  = snap.Objects.FirstOrDefault(o =>
                        o.Id.ToString() == aliasOrId || o.Alias == aliasOrId);
                    return obj is null
                        ? JsonSerializer.Serialize(new { error = $"Object not found: {aliasOrId}" })
                        : JsonSerializer.Serialize(new
                        {
                            id     = obj.Id.ToString(),
                            kind   = obj.Kind,
                            alias  = obj.Alias,
                            x      = obj.X,
                            y      = obj.Y,
                            width  = obj.Width,
                            height = obj.Height,
                            zIndex = obj.ZIndex,
                            locked = obj.IsLocked,
                        });
                },
                "get_object_details",
                "获取单个对象的详细信息。aliasOrId 可以是对象别名或 GUID。"),
            ToolCapabilityInfo.Safe());

        registry.Register(
            AIFunctionFactory.Create(
                async (CancellationToken ct = default) =>
                {
                    var snap = await actions.GetSnapshotAsync(ct);
                    return JsonSerializer.Serialize(new
                    {
                        pageId  = snap.PageId,
                        version = snap.Version,
                        objects = snap.Objects.Count,
                        width   = snap.Width,
                        height  = snap.Height,
                    });
                },
                "get_page_info",
                "获取当前页面信息：页面ID、版本号、对象数量、画布尺寸。"),
            ToolCapabilityInfo.Safe());
    }
}
