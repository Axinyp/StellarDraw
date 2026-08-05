using Axinyp.Wpf.Hosting.Abstractions.Modules;
using Axinyp.Wpf.Hosting.Abstractions.Plugins;

namespace Axinyp.Wpf.Hosting.Internal.Plugins;

/// <summary>绑定到 HostApplicationBuilder 的启动期插件状态；Host Dispose 后负责释放所有插件 ALC。</summary>
internal sealed class StartupPluginState : IDisposable
{
    private readonly List<StartupPluginRuntime> _runtimes = [];
    private readonly List<PluginDescriptor> _plugins = [];
    private readonly List<PluginLoadFailure> _failures = [];
    private readonly List<StartupPluginDiagnostic> _diagnostics = [];

    public IEnumerable<IModule> Modules => _runtimes.SelectMany(runtime => runtime.Modules);

    public void Add(
        PluginCatalog catalog,
        StartupPluginRuntime runtime,
        IEnumerable<StartupPluginDiagnostic> diagnostics)
    {
        _plugins.AddRange(catalog.Plugins);
        _failures.AddRange(catalog.Failures);
        _runtimes.Add(runtime);
        _diagnostics.AddRange(diagnostics);
    }

    public IPluginCatalog CreateCatalog() => new PluginCatalog(_plugins, _failures);

    public void LogFailures(ILogger logger)
    {
        try
        {
            foreach (var failure in _failures)
            {
                var metadata = failure.Metadata;
                var diagnostic = _diagnostics.FirstOrDefault(candidate => ReferenceEquals(candidate.Failure, failure));
                logger.LogWarning(
                    PluginDiscoveryEventIds.DiscoveryFailure,
                    diagnostic?.Exception,
                    "启动期插件发现跳过目录 {PluginDirectory} 的插件 {PluginId}：{FailureKind} ({DiagnosticCode})。EntryAssembly={EntryAssembly}; DependencyId={DependencyId}; ExceptionType={ExceptionType}",
                    failure.Directory,
                    failure.Id,
                    failure.Kind,
                    metadata?.DiagnosticCode,
                    metadata?.EntryAssembly,
                    metadata?.DependencyId,
                    metadata?.ExceptionType);
            }
        }
        finally
        {
            _diagnostics.Clear();
        }
    }

    public void Dispose()
    {
        _diagnostics.Clear();
        foreach (var runtime in _runtimes.AsEnumerable().Reverse())
            runtime.Dispose();
        _runtimes.Clear();
    }
}
