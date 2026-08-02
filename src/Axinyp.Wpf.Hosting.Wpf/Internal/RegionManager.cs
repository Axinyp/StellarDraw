using System.Diagnostics;
using System.Windows.Controls;

namespace Axinyp.Wpf.Hosting.Wpf.Internal;

/// <summary>显式内容区域注册表。区域名在整个宿主范围内唯一。</summary>
internal sealed class RegionManager(IUiDispatcher uiDispatcher) : IRegionManager
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RegionHost> _regions = new(StringComparer.Ordinal);

    public IDisposable RegisterRegion(string name, ContentControl host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(host);
        if (!host.Dispatcher.CheckAccess())
            throw new InvalidOperationException("区域必须在其所属 WPF Dispatcher 上注册。");

        var region = new RegionHost(name, host);
        lock (_gate)
        {
            if (!_regions.TryAdd(name, region))
                throw new InvalidOperationException($"区域 '{name}' 已被注册。");
        }
        return new RegionRegistration(this, name, region);
    }

    public bool IsRegistered(string name)
    {
        lock (_gate) return _regions.ContainsKey(name);
    }

    public IRegion GetRegion(string name) => GetHost(name);

    internal RegionHost GetHost(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_gate)
            return _regions.TryGetValue(name, out var region)
                ? region
                : throw new InvalidOperationException($"区域 '{name}' 未注册。");
    }

    private void Unregister(string name, RegionHost region)
    {
        lock (_gate)
        {
            if (_regions.TryGetValue(name, out var current) && ReferenceEquals(current, region))
                _regions.Remove(name);
            else return;
        }

        if (uiDispatcher.IsShutdown(region.Dispatcher))
        {
            DisposeAfterShutdown(region);
            return;
        }

        if (uiDispatcher.CheckAccess(region.Dispatcher))
        {
            region.Dispose();
            return;
        }

        try
        {
            ObserveScheduledDisposal(region, uiDispatcher.InvokeAsync(region.Dispatcher, region.Dispose));
        }
        catch (InvalidOperationException) when (uiDispatcher.IsShutdown(region.Dispatcher))
        {
            DisposeAfterShutdown(region);
        }
    }

    private void ObserveScheduledDisposal(RegionHost region, Task disposal)
    {
        _ = disposal.ContinueWith(
            completed =>
            {
                if (completed.IsCompletedSuccessfully)
                    return;

                if (uiDispatcher.IsShutdown(region.Dispatcher))
                {
                    DisposeAfterShutdown(region);
                    return;
                }

                Trace.TraceError(
                    "区域 '{0}' 的 Dispatcher 清理失败：{1}",
                    region.Name,
                    completed.Exception);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void DisposeAfterShutdown(RegionHost region)
    {
        try
        {
            region.Dispose(detachVisual: false);
        }
        catch (Exception exception)
        {
            Trace.TraceError("区域 '{0}' 的关闭期资源释放失败：{1}", region.Name, exception);
        }
    }

    private sealed class RegionRegistration(RegionManager manager, string name, RegionHost region) : IDisposable
    {
        private RegionManager? _manager = manager;
        public void Dispose() => Interlocked.Exchange(ref _manager, null)?.Unregister(name, region);
    }
}
