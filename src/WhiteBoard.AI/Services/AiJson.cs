using System.Text.Encodings.Web;
using System.Text.Json;

namespace WhiteBoard.AI.Services;

/// <summary>
/// 面向 LLM 反馈与 UI 展示的 JSON 序列化选项。
/// 使用 <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>，不把中文等非 ASCII 字符
/// 转义成 \uXXXX，使工具入参/结果在气泡里直接可读（如 "字" 而非 "字"）。
/// 输出仅用于 LLM 上下文与本地 UI，不注入 HTML，故 relaxed 转义是安全的。
/// </summary>
public static class AiJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
