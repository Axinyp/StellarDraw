using Microsoft.Extensions.AI;
using WhiteBoard.AI.Services;

namespace WhiteBoard.AI.Tools.Catalogs;

/// <summary>编辑工具集：撤销、重做、清空、删除、移动、选择。</summary>
public static class EditingToolCatalog
{
    public static void Register(WhiteboardToolRegistry registry, IWhiteboardAiActions actions)
    {
        registry.Register(
            AIFunctionFactory.Create(
                async (string? groupId = null, CancellationToken ct = default) =>
                    (await actions.UndoAsync(groupId, ct)).ToJson(),
                "undo",
                "撤销上一步操作。如果提供 groupId，则整组 AI 操作一起撤销。"),
            ToolCapabilityInfo.Safe());

        registry.Register(
            AIFunctionFactory.Create(
                async (CancellationToken ct = default) =>
                    (await actions.RedoAsync(ct)).ToJson(),
                "redo",
                "重做上一步被撤销的操作。"),
            ToolCapabilityInfo.Safe());

        registry.Register(
            AIFunctionFactory.Create(
                async (CancellationToken ct = default) =>
                    (await actions.ClearPageAsync(ct)).ToJson(),
                "clear_page",
                "清空当前页面的全部内容。此操作不可逆（可通过 undo 恢复）。"),
            ToolCapabilityInfo.Destructive());

        registry.Register(
            AIFunctionFactory.Create(
                async (
                    string[] aliasesOrIds,
                    string? groupId = null,
                    CancellationToken ct = default) =>
                    (await actions.DeleteObjectsAsync(aliasesOrIds, groupId, ct)).ToJson(),
                "delete_objects",
                "删除指定对象。aliasesOrIds 为对象别名或 GUID 列表。"),
            ToolCapabilityInfo.Destructive());

        registry.Register(
            AIFunctionFactory.Create(
                async (
                    string[] aliasesOrIds,
                    float dx,
                    float dy,
                    string? groupId = null,
                    CancellationToken ct = default) =>
                    (await actions.MoveObjectsAsync(aliasesOrIds, dx, dy, groupId, ct)).ToJson(),
                "move_objects",
                "将指定对象平移 (dx, dy) 像素。正值向右/向下，负值向左/向上。"),
            ToolCapabilityInfo.Safe());

        registry.Register(
            AIFunctionFactory.Create(
                async (
                    float x, float y, float width, float height,
                    CancellationToken ct = default) =>
                    (await actions.SelectObjectsAsync(x, y, width, height, ct)).ToJson(),
                "select_objects_in_region",
                "选中指定矩形区域内的所有对象。返回被选中的对象 ID 列表。"),
            ToolCapabilityInfo.Safe());
    }
}