using Microsoft.Extensions.Hosting;

namespace Axinyp.Wpf.Hosting.Internal.Plugins;

/// <summary>包装 Generic Host，确保先释放 DI 容器再卸载插件 ALC。</summary>
internal sealed class PluginAwareHost(IHost host, IDisposable pluginResources) : IHost, IAsyncDisposable
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

    /// <summary>
    /// Generic Host 自身实现 IAsyncDisposable；若这里不转发，`await using` 与
    /// HostingAbstractionsHostExtensions 的异步释放路径都会退化为 sync-over-async 的
    /// <see cref="IDisposable.Dispose"/>，在 WPF UI 线程上有死锁风险。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        var host = Interlocked.Exchange(ref _host, null);
        var resources = Interlocked.Exchange(ref _pluginResources, null);
        try
        {
            if (host is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else
                host?.Dispose();
        }
        finally
        {
            resources?.Dispose();
        }
    }
}
