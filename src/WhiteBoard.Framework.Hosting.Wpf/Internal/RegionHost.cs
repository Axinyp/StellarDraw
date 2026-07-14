using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace WhiteBoard.Framework.Hosting.Wpf.Internal;

/// <summary>单个 ContentControl 区域及其当前组合和后退栈。</summary>
internal sealed class RegionHost(string name, ContentControl host) : IRegion, IDisposable
{
    private ViewHandle? _active;
    private bool _disposed;

    public string Name { get; } = name;

    public ContentControl Host { get; } = host;

    public Dispatcher Dispatcher => Host.Dispatcher;

    public FrameworkElement? ActiveView => _active?.View;

    public object? ActiveViewModel => _active?.ViewModel;

    public Type? ActiveViewModelType => _active?.ViewModelType;

    public object? ActiveParameter { get; private set; }

    public Stack<NavigationJournalEntry> BackStack { get; } = [];

    public void Replace(ViewHandle next, object? parameter)
    {
        ThrowIfDisposed();
        var previous = _active;
        if (previous is not null && ReferenceEquals(Host.Content, previous.View))
            Host.Content = null;

        _active = next;
        ActiveParameter = parameter;
        Host.Content = next.View;
        previous?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_active is not null && ReferenceEquals(Host.Content, _active.View))
            Host.Content = null;
        _active?.Dispose();
        _active = null;
        BackStack.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException($"Region '{Name}'");
    }
}
