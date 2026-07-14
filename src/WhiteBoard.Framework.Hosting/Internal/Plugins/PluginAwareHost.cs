using Microsoft.Extensions.Hosting;

namespace WhiteBoard.Framework.Hosting.Internal.Plugins;

/// <summary>包装 Generic Host，确保先释放 DI 容器再卸载插件 ALC。</summary>
internal sealed class PluginAwareHost(IHost host, IDisposable pluginResources) : IHost
{
    private IHost? _host = host;
    private IDisposable? _pluginResources = pluginResources;

    public IServiceProvider Services
        => _host?.Services ?? throw new ObjectDisposedException(nameof(PluginAwareHost));

    public Task StartAsync(CancellationToken cancellationToken = default)
        => (_host ?? throw new ObjectDisposedException(nameof(PluginAwareHost))).StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default)
        => (_host ?? throw new ObjectDisposedException(nameof(PluginAwareHost))).StopAsync(cancellationToken);

    public void Dispose()
    {
        var host = Interlocked.Exchange(ref _host, null);
        var resources = Interlocked.Exchange(ref _pluginResources, null);
        try { host?.Dispose(); }
        finally { resources?.Dispose(); }
    }
}
