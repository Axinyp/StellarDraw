namespace WhiteBoard.Framework.Hosting.Plugins;

/// <summary>启动期插件目录的只读发现结果。</summary>
public interface IPluginCatalog
{
    IReadOnlyList<PluginDescriptor> Plugins { get; }

    IReadOnlyList<PluginLoadFailure> Failures { get; }
}
