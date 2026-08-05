namespace Axinyp.Wpf.Hosting.Abstractions.Plugins;

/// <summary>
/// 插件清单（plugin.json）。入口程序集必须位于清单所在目录，Dependencies 仅定义插件启动期加载顺序。
/// </summary>
public sealed record PluginManifest
{
    /// <summary>
    /// 插件唯一标识。
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 插件版本字符串。
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// 相对插件目录的入口程序集文件名。
    /// </summary>
    public required string EntryAssembly { get; init; }

    /// <summary>
    /// 当前插件声明的直接依赖插件 ID 列表。
    /// </summary>
    public IReadOnlyList<string> Dependencies { get; init; } = [];
}
