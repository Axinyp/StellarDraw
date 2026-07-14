namespace WhiteBoard.Framework.Hosting.Plugins;

/// <summary>插件发现失败类别，用于 UI 或日志以结构化方式呈现诊断。</summary>
public enum PluginLoadFailureKind
{
    ManifestMissing,
    ManifestInvalid,
    DuplicatePluginId,
    MissingDependency,
    DependencyCycle,
    DependencyFailed,
    EntryAssemblyMissing,
    AssemblyLoadFailed,
    ModuleDiscoveryFailed,
    ModuleActivationFailed
}
