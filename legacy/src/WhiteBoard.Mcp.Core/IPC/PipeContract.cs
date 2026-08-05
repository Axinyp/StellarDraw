using System.Text.Json.Serialization;

namespace WhiteBoard.Mcp.Core.IPC;

// All messages are System.Text.Json-serialized, newline-delimited (NDJSON).

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PipeListToolsRequest), "list_tools")]
[JsonDerivedType(typeof(PipeListToolsResponse), "list_tools_result")]
[JsonDerivedType(typeof(PipeInvokeToolRequest), "invoke_tool")]
[JsonDerivedType(typeof(PipeInvokeToolResponse), "invoke_tool_result")]
public abstract record PipeMessage(string Type);

public record PipeListToolsRequest(string Token) : PipeMessage("list_tools");

public record PipeListToolsResponse(
    IReadOnlyList<PipeToolInfo> Tools) : PipeMessage("list_tools_result");

public record PipeInvokeToolRequest(
    string Token,
    string Name,
    IDictionary<string, object?> Args) : PipeMessage("invoke_tool");

public record PipeInvokeToolResponse(
    bool Success,
    string Json,
    string? Error) : PipeMessage("invoke_tool_result");

public record PipeToolInfo(
    string Name,
    string Description,
    bool IsDestructive,
    string JsonSchema);