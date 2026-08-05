using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Axinyp.Wpf.Hosting.Wpf.Internal;

/// <summary>单个 ContentControl 区域及其当前组合和后退栈。</summary>
internal sealed class RegionHost(string name, ContentControl host) : IRegion, IDisposable
{
    private ViewHandle? _active;
    private bool _disposed;
    private int _backStackCount;

    public string Name { get; } = name;

    public ContentControl Host { get; } = host;

    public Dispatcher Dispatcher => Host.Dispatcher;

    public FrameworkElement? ActiveView => _active?.View;

    public object? ActiveViewModel => _active?.ViewModel;

    public Type? ActiveViewModelType => _active?.ViewModelType;

    public object? ActiveParameter { get; private set; }

    private Stack<NavigationJournalEntry> BackStack { get; } = [];

    internal SemaphoreSlim NavigationGate { get; } = new(1, 1);

    public bool CanGoBack => Volatile.Read(ref _backStackCount) > 0;

    public void PushBack(NavigationJournalEntry entry)
    {
        BackStack.Push(entry);
        Volatile.Write(ref _backStackCount, BackStack.Count);
    }

    public NavigationJournalEntry PeekBack() => BackStack.Peek();

    public void PopBack()
    {
        BackStack.Pop();
        Volatile.Write(ref _backStackCount, BackStack.Count);
    }

    public Exception? Replace(ViewHandle next, object? parameter)
    {
        ThrowIfDisposed();
        var previous = _active;
        if (previous is not null && ReferenceEquals(Host.Content, previous.View))
            Host.Content = null;

        _active = next;
        ActiveParameter = parameter;
        Host.Content = next.View;

        try
        {
            previous?.Dispose();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    public void Dispose() => Dispose(detachVisual: true);

    public void Dispose(bool detachVisual)
    {
        if (_disposed) return;
        _disposed = true;
        if (detachVisual && _active is not null && ReferenceEquals(Host.Content, _active.View))
            Host.Content = null;
        _active?.Dispose(detachVisual);
        _active = null;
        BackStack.Clear();
        Volatile.Write(ref _backStackCount, 0);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException($"Region '{Name}'");
    }
}
