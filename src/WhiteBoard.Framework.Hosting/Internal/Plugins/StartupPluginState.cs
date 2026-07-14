using WhiteBoard.Framework.Hosting.Plugins;

namespace WhiteBoard.Framework.Hosting.Internal.Plugins;

/// <summary>绑定到 HostApplicationBuilder 的启动期插件状态；Host Dispose 后负责释放所有插件 ALC。</summary>
internal sealed class StartupPluginState : IDisposable
{
    private readonly List<StartupPluginRuntime> _runtimes = [];
    private readonly List<PluginDescriptor> _plugins = [];
    private readonly List<PluginLoadFailure> _failures = [];

    public IEnumerable<Modules.IModule> Modules => _runtimes.SelectMany(runtime => runtime.Modules);

    public void Add(PluginCatalog catalog, StartupPluginRuntime runtime)
    {
        _plugins.AddRange(catalog.Plugins);
        _failures.AddRange(catalog.Failures);
        _runtimes.Add(runtime);
    }

    public IPluginCatalog CreateCatalog() => new PluginCatalog(_plugins, _failures);

    public void Dispose()
    {
        foreach (var runtime in _runtimes.AsEnumerable().Reverse())
            runtime.Dispose();
        _runtimes.Clear();
    }
}
