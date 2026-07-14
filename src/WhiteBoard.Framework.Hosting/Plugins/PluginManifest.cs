namespace WhiteBoard.Framework.Hosting.Plugins;

/// <summary>
/// 插件清单（plugin.json）。入口程序集必须位于清单所在目录，Dependencies 仅定义插件启动期加载顺序。
/// </summary>
public sealed record PluginManifest
{
    public required string Id { get; init; }

    public required string Version { get; init; }

    public required string EntryAssembly { get; init; }

    public IReadOnlyList<string> Dependencies { get; init; } = [];
}
