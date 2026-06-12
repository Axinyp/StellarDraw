using System.Text.Json;
using WhiteBoard.AI.Services;

namespace WhiteBoard.AI.Session;

/// <summary>
/// 工具调用结果归一化器。
/// 本地工具（返回 ToolResult 或 JSON string）与外部 MCP 工具（返回 string/object）
/// 统一序列化为 LLM 可理解的 JSON 字符串。
/// </summary>
internal static class ToolResultEnvelope
{
    internal static string Normalize(object? result) => result switch
    {
        string s     => string.IsNullOrEmpty(s) ? """{"status":"ok"}""" : s,
        ToolResult r => r.ToJson(),
        null         => """{"status":"ok"}""",
        _            => JsonSerializer.Serialize(result)
    };
}
