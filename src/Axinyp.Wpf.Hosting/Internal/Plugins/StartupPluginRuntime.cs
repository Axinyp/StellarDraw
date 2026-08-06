using Axinyp.Wpf.Hosting.Abstractions.Modules;
using Axinyp.Wpf.Hosting.Abstractions.Plugins;

namespace Axinyp.Wpf.Hosting.Internal.Plugins;

/// <summary>仅在 Host 存活期间保存插件 ALC 与模块实例；Host Dispose 后统一卸载。</summary>
internal sealed class StartupPluginRuntime(IReadOnlyList<StartupPluginLoadHandle> handles) : IDisposable
{
    public IReadOnlyList<StartupPluginLoadHandle> Handles => handles;

    public IReadOnlyList<IModule> Modules
        => handles.SelectMany(handle => handle.Modules).ToList();

    public void Dispose()
    {
        foreach (var handle in handles.Reverse())
            handle.Dispose();
    }
}

internal sealed class StartupPluginLoadHandle(
    PluginDescriptor descriptor,
    StartupPluginLoadContext loadContext,
    IReadOnlyList<IModule> modules) : IDisposable
{
    private StartupPluginLoadContext? _loadContext = loadContext;
    private IReadOnlyList<IModule>? _modules = modules;

    public PluginDescriptor Descriptor { get; } = descriptor;

    public IReadOnlyList<IModule> Modules => _modules ?? [];

    public void Dispose()
    {
        _modules = null;
        var context = Interlocked.Exchange(ref _loadContext, null);
        context?.Unload();
    }
}
