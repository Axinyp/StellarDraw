using System.Windows.Threading;
using Axinyp.Wpf.Hosting.Abstractions.Wpf;

namespace Axinyp.Wpf.Hosting.Wpf.Tests;

internal sealed class RejectingUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public Dispatcher GetApplicationDispatcher() => dispatcher;

    public bool CheckAccess(Dispatcher target) => target.CheckAccess();

    public bool IsShutdown(Dispatcher target) => true;

    public Task InvokeAsync(
        Dispatcher target,
        Action action,
        DispatcherPriority priority = DispatcherPriority.Normal)
        => Task.FromException(CreateException());

    public Task<T> InvokeAsync<T>(
        Dispatcher target,
        Func<T> action,
        DispatcherPriority priority = DispatcherPriority.Normal)
        => Task.FromException<T>(CreateException());

    public Task<T> InvokeAsync<T>(
        Dispatcher target,
        Func<Task<T>> action,
        DispatcherPriority priority = DispatcherPriority.Normal)
        => Task.FromException<T>(CreateException());

    private static InvalidOperationException CreateException()
        => new("WPF Dispatcher 正在关闭，不能调度新的 UI 操作。");
}
