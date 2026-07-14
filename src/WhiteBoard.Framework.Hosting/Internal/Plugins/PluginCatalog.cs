using WhiteBoard.Framework.Hosting.Plugins;

namespace WhiteBoard.Framework.Hosting.Internal.Plugins;

/// <summary>只保存不可泄漏 ALC 引用的插件发现目录。</summary>
internal sealed class PluginCatalog(
    IReadOnlyList<PluginDescriptor> plugins,
    IReadOnlyList<PluginLoadFailure> failures) : IPluginCatalog
{
    public IReadOnlyList<PluginDescriptor> Plugins { get; } = plugins;

    public IReadOnlyList<PluginLoadFailure> Failures { get; } = failures;
}
