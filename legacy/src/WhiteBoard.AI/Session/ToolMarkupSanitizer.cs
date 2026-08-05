using System.Text;
using System.Text.RegularExpressions;

namespace WhiteBoard.AI.Session;

/// <summary>
/// 清洗部分模型（如未正确支持原生 function-calling 的模型）把工具调用
/// 序列化成文本标记（如 <c>&lt;|DSML|parameter name="x"&gt;</c>、<c>&lt;/|DSML|parameter&gt;</c>）
/// 泄漏进正文的情况，避免这些标记出现在对话气泡里。
///
/// 注意：这是对"模型工具调用能力不足"的降级缓解，根治办法是改用支持原生工具调用的模型。
/// </summary>
internal static partial class ToolMarkupSanitizer
{
    // 匹配形如 <|...>、</|...> 的泄漏标记（以 <| 或 </| 开头，到下一个 > 结束）
    [GeneratedRegex(@"</?\|[^>]*>", RegexOptions.Compiled)]
    private static partial Regex TokenRegex();

    /// <summary>对完整文本做一次性清洗（用于重建历史消息，避免污染上下文）。</summary>
    public static string Clean(string text) =>
        string.IsNullOrEmpty(text) ? text : TokenRegex().Replace(text, "");
}

/// <summary>
/// 流式清洗器：逐片段接收 LLM 文本，剥离完整的泄漏标记，并缓存可能被截断的尾部
/// （如 "&lt;|DSM" 尚未闭合），等后续片段补全后再判定，避免把半个标记当正文输出。
/// 非线程安全，单个流内串行使用。
/// </summary>
internal sealed class StreamingToolMarkupSanitizer
{
    private readonly StringBuilder _buffer = new();

    /// <summary>压入一个文本片段，返回当前可安全输出的、已清洗的文本。</summary>
    public string Push(string chunk)
    {
        if (!string.IsNullOrEmpty(chunk)) _buffer.Append(chunk);
        var s = _buffer.ToString();

        // 若存在未闭合的 '<'（其后没有 '>'），说明可能是被截断的标记，保留到缓冲等待补全。
        int lastOpen = s.LastIndexOf('<');
        int safeLen = (lastOpen >= 0 && s.IndexOf('>', lastOpen) < 0) ? lastOpen : s.Length;

        var emit = s[..safeLen];
        var keep = s[safeLen..];
        _buffer.Clear();
        _buffer.Append(keep);

        return ToolMarkupSanitizer.Clean(emit);
    }

    /// <summary>流结束时冲刷剩余缓冲（清洗掉残留标记）。</summary>
    public string Flush()
    {
        var s = _buffer.ToString();
        _buffer.Clear();
        return ToolMarkupSanitizer.Clean(s);
    }
}
