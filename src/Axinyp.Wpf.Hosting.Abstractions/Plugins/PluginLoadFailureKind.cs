namespace Axinyp.Wpf.Hosting.Abstractions.Plugins;

/// <summary>插件发现失败类别，用于 UI 或日志以结构化方式呈现诊断。</summary>
public enum PluginLoadFailureKind
{
    /// <summary>插件目录中缺少 plugin.json。</summary>
    ManifestMissing,

    /// <summary>plugin.json 存在但内容无法解析或字段无效。</summary>
    ManifestInvalid,

    /// <summary>多个插件声明了重复的插件 ID。</summary>
    DuplicatePluginId,

    /// <summary>插件声明依赖某个不存在的插件。</summary>
    MissingDependency,

    /// <summary>插件依赖关系存在环。</summary>
    DependencyCycle,

    /// <summary>插件依赖项本身加载失败，导致当前插件无法继续加载。</summary>
    DependencyFailed,

    /// <summary>清单声明的入口程序集文件不存在。</summary>
    EntryAssemblyMissing,

    /// <summary>入口程序集加载失败。</summary>
    AssemblyLoadFailed,

    /// <summary>程序集已加载，但未能发现有效模块类型。</summary>
    ModuleDiscoveryFailed,

    /// <summary>模块类型已发现，但实例化失败。</summary>
    ModuleActivationFailed
}
