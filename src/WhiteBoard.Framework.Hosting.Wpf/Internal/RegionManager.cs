using System.Windows.Controls;

namespace WhiteBoard.Framework.Hosting.Wpf.Internal;

/// <summary>显式内容区域注册表。区域名在整个宿主范围内唯一。</summary>
internal sealed class RegionManager : IRegionManager
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
        if (region.Dispatcher.HasShutdownStarted || region.Dispatcher.HasShutdownFinished)
            return;
        if (region.Dispatcher.CheckAccess()) region.Dispose();
        else region.Dispatcher.Invoke(region.Dispose);
    }

    private sealed class RegionRegistration(RegionManager manager, string name, RegionHost region) : IDisposable
    {
        private RegionManager? _manager = manager;
        public void Dispose() => Interlocked.Exchange(ref _manager, null)?.Unregister(name, region);
    }
}
