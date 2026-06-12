using Microsoft.Extensions.AI;
using WhiteBoard.AI.Services;

namespace WhiteBoard.AI.Tools.Catalogs;

/// <summary>
/// 导航工具集：页面管理（添加/切换/查询）和视图控制（缩放/重置）。
/// 视口操作（zoom_in/out/reset_view）需通过 RegisterViewportControls 注入 UI 回调。
/// </summary>
public static class NavigationToolCatalog
{
    public static void Register(WhiteboardToolRegistry registry, IWhiteboardAiActions actions)
    {
        registry.Register(
            AIFunctionFactory.Create(
                async (string? title = null, CancellationToken ct = default) =>
                    (await actions.AddPageAsync(title, ct)).ToJson(),
                "add_page",
                "在白板末尾添加新页面并切换到该页面。title 为可选页面标题。"),
            ToolCapabilityInfo.Safe());

        registry.Register(
            AIFunctionFactory.Create(
                async (int pageIndex, CancellationToken ct = default) =>
                    (await actions.NavigateToPageAsync(pageIndex, ct)).ToJson(),
                "navigate_page",
                "切换到指定页面（pageIndex 从 0 开始）。切换成功返回 ok，索引越界返回 error。"),
            ToolCapabilityInfo.Safe());

        registry.Register(
            AIFunctionFactory.Create(
                async (CancellationToken ct = default) =>
                    await actions.GetPagesInfoAsync(ct),
                "get_page_count",
                "获取白板总页数、当前页索引及各页信息（id、title）。"),
            ToolCapabilityInfo.Safe());
    }

    /// <summary>
    /// 注册视口控制工具（zoom_in / zoom_out / reset_view）。
    /// 应由 UI 层在初始化时调用，传入实际操作 InkSurface 的回调。
    /// </summary>
    public static void RegisterViewportControls(
        WhiteboardToolRegistry registry,
        Action?                zoomIn    = null,
        Action?                zoomOut   = null,
        Action?                resetView = null)
    {
        if (zoomIn is not null)
            registry.Register(
                AIFunctionFactory.Create(
                    () => { zoomIn(); return """{"status":"ok","action":"zoom_in"}"""; },
                    "zoom_in",
                    "放大当前视图（仅影响缩放，不修改白板内容）。"),
                ToolCapabilityInfo.Safe());

        if (zoomOut is not null)
            registry.Register(
                AIFunctionFactory.Create(
                    () => { zoomOut(); return """{"status":"ok","action":"zoom_out"}"""; },
                    "zoom_out",
                    "缩小当前视图（仅影响缩放，不修改白板内容）。"),
                ToolCapabilityInfo.Safe());

        if (resetView is not null)
            registry.Register(
                AIFunctionFactory.Create(
                    () => { resetView(); return """{"status":"ok","action":"reset_view"}"""; },
                    "reset_view",
                    "重置视图缩放和平移，恢复到默认视角（100% 缩放，原点对齐）。"),
                ToolCapabilityInfo.Safe());
    }
}
