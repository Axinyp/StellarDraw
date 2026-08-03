namespace Axinyp.Wpf.Hosting.Abstractions.Plugins;

/// <summary>一个插件目录的非致命加载失败。失败插件被跳过，其他插件仍会继续发现。</summary>
public sealed record PluginLoadFailure(string Directory, string? Id, PluginLoadFailureKind Kind, string Message)
{
    /// <summary>可安全持久化与展示的稳定诊断上下文，不包含原始异常。</summary>
    public PluginLoadFailureMetadata? Metadata { get; init; }
}
