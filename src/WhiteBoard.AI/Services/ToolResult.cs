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
        // 用 JsonSerializer 而非手写拼接，确保 ErrorMessage / alias / id 中的特殊字符
        // （引号、反斜杠、换行）被正确转义，避免向 LLM 返回非法 JSON。
        var payload = new
        {
            status = Status,
            error = ErrorMessage,
            created = CreatedObjectIds,
            updated = UpdatedObjectIds,
            deleted = DeletedObjectIds,
            aliases = Aliases,
            undoGroupId = UndoGroupId,
            pageId = PageId,
            pageVersion = PageVersion,
        };
        return System.Text.Json.JsonSerializer.Serialize(payload);
    }
}