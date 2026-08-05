namespace Axinyp.Wpf.Hosting.Abstractions.Plugins;

/// <summary>启动期插件目录的只读发现结果。</summary>
public interface IPluginCatalog
{
    /// <summary>
    /// 成功发现并通过依赖校验的插件集合。
    /// </summary>
    IReadOnlyList<PluginDescriptor> Plugins { get; }

    /// <summary>
    /// 发现或加载失败的插件诊断集合。
    /// </summary>
    IReadOnlyList<PluginLoadFailure> Failures { get; }
}
