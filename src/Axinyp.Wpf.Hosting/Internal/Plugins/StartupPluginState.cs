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
    private readonly HashSet<string> _pluginIds = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<IModule> Modules => _runtimes.SelectMany(runtime => runtime.Modules);

    /// <summary>
    /// 并入一次发现结果，并返回本次被接受插件的模块集合。
    /// 多次 AddStartupPlugins 指向重叠目录时，同一插件 Id 只接受首个：
    /// 后到者记为 <see cref="PluginLoadFailureKind.DuplicatePluginId"/> 失败、模块不注册
    /// （其 ALC 仍由本状态在 Host Dispose 时统一卸载），避免服务被静默注册两遍。
    /// </summary>
    public IReadOnlyList<IModule> Add(
        PluginCatalog catalog,
        StartupPluginRuntime runtime,
        IEnumerable<StartupPluginDiagnostic> diagnostics)
    {
        var duplicateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in catalog.Plugins)
        {
            if (_pluginIds.Add(plugin.Id))
            {
                _plugins.Add(plugin);
                continue;
            }

            duplicateIds.Add(plugin.Id);
            _failures.Add(new PluginLoadFailure(plugin.Directory, plugin.Id,
                PluginLoadFailureKind.DuplicatePluginId,
                "插件 Id 已由先前的 AddStartupPlugins 调用注册，本次发现的副本被跳过。"));
        }

        _failures.AddRange(catalog.Failures);
        _runtimes.Add(runtime);
        _diagnostics.AddRange(diagnostics);

        return runtime.Handles
            .Where(handle => !duplicateIds.Contains(handle.Descriptor.Id))
            .SelectMany(handle => handle.Modules)
            .ToList();
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
