namespace WhiteBoard.AI.Services;

/// <summary>
/// AI 工具调用结果契约。
/// AgentLoop 将此结果序列化为 JSON 反馈给 LLM，LLM 据此进行多步推理（如引用 objectId 连线）。
/// </summary>
public sealed record ToolResult(
    string Status,           // ok | error | confirm_required | partial
    string? ErrorMessage,
    IReadOnlyList<string> CreatedObjectIds,
    IReadOnlyList<string> UpdatedObjectIds,
    IReadOnlyList<string> DeletedObjectIds,
    IReadOnlyDictionary<string, string> Aliases,   // { "alias_name": "guid" }
    string? UndoGroupId,
    string? PageId,
    long PageVersion,
    IReadOnlyList<string> Warnings)
{
    public static ToolResult Ok(
        IReadOnlyList<string>? created = null,
        IReadOnlyList<string>? updated = null,
        IReadOnlyList<string>? deleted = null,
        IReadOnlyDictionary<string, string>? aliases = null,
        string? groupId = null,
        string? pageId = null,
        long version = 0) =>
        new(
            "ok", null,
            created ?? [],
            updated ?? [],
            deleted ?? [],
            aliases ?? new Dictionary<string, string>(),
            groupId, pageId, version, []);

    public static ToolResult Error(string message) =>
        new("error", message, [], [], [], new Dictionary<string, string>(),
            null, null, 0, []);

    public string ToJson()
    {
        var aliasStr = string.Join(", ", Aliases.Select(kv => $"\"{kv.Key}\":\"{kv.Value}\""));
        return $$"""
            {
              "status": "{{Status}}",
              "error": {{(ErrorMessage is null ? "null" : $"\"{ErrorMessage}\"")}},
              "created": [{{string.Join(",", CreatedObjectIds.Select(id => $"\"{id}\""))}}],
              "updated": [{{string.Join(",", UpdatedObjectIds.Select(id => $"\"{id}\""))}}],
              "deleted": [{{string.Join(",", DeletedObjectIds.Select(id => $"\"{id}\""))}}],
              "aliases": { {{aliasStr}} },
              "undoGroupId": {{(UndoGroupId is null ? "null" : $"\"{UndoGroupId}\"")}},
              "pageId": {{(PageId is null ? "null" : $"\"{PageId}\"")}},
              "pageVersion": {{PageVersion}}
            }
            """;
    }
}