using System.Windows;
using System.Windows.Threading;

namespace Axinyp.Wpf.Hosting.Wpf.Internal;

/// <summary>WPF Application Dispatcher 的默认适配器。</summary>
internal sealed class WpfUiDispatcher : IUiDispatcher
{
    public Dispatcher GetApplicationDispatcher() => Application.Current?.Dispatcher
        ?? throw new InvalidOperationException("当前没有可用的 WPF Application Dispatcher。");

    public bool CheckAccess(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        return dispatcher.CheckAccess();
    }

    public bool IsShutdown(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        return dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished;
    }

    public Task InvokeAsync(Dispatcher dispatcher, Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (CheckAccess(dispatcher))
        {
            ThrowIfShutdown(dispatcher);
            action();
            return Task.CompletedTask;
        }

        ThrowIfShutdown(dispatcher);
        return dispatcher.InvokeAsync(action, priority).Task;
    }

    public Task<T> InvokeAsync<T>(Dispatcher dispatcher, Func<T> action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (CheckAccess(dispatcher))
        {
            ThrowIfShutdown(dispatcher);
            return Task.FromResult(action());
        }

        ThrowIfShutdown(dispatcher);
        return dispatcher.InvokeAsync(action, priority).Task;
    }

    public Task<T> InvokeAsync<T>(Dispatcher dispatcher, Func<Task<T>> action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (CheckAccess(dispatcher))
        {
            ThrowIfShutdown(dispatcher);
            return action();
        }

        ThrowIfShutdown(dispatcher);
        return dispatcher.InvokeAsync(action, priority).Task.Unwrap();
    }

    private static void ThrowIfShutdown(Dispatcher dispatcher)
    {
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            throw new InvalidOperationException("WPF Dispatcher 正在关闭，不能调度新的 UI 操作。");
    }
}
