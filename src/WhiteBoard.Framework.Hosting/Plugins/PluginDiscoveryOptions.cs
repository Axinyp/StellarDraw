namespace WhiteBoard.Framework.Hosting.Plugins;

/// <summary>插件发现选项。插件代码被视作受信任代码；AssemblyLoadContext 只做依赖隔离，不提供安全沙箱。</summary>
public sealed class PluginDiscoveryOptions
{
    /// <summary>包含各插件子目录的根目录。每个子目录应包含 plugin.json。</summary>
    public IList<string> PluginDirectories { get; } = [];

    /// <summary>需要与宿主 Default ALC 共享的额外契约程序集。</summary>
    public IList<System.Reflection.Assembly> AdditionalSharedAssemblies { get; } = [];
}
