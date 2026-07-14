namespace WhiteBoard.Framework.Hosting.Plugins;

/// <summary>一个插件目录的非致命加载失败。失败插件被跳过，其他插件仍会继续发现。</summary>
public sealed record PluginLoadFailure(string Directory, string? Id, PluginLoadFailureKind Kind, string Message);
