using System.Windows.Threading;

namespace Axinyp.Wpf.Hosting.Abstractions.Wpf;

/// <summary>
/// WPF Dispatcher 的可替换调度边界。
/// 所有操作在 Dispatcher 开始关闭后均拒绝排队，调用方必须观察返回任务。
/// </summary>
public interface IUiDispatcher
{
    /// <summary>获取当前 WPF Application 的 Dispatcher。</summary>
    Dispatcher GetApplicationDispatcher();

    /// <summary>判断调用方是否拥有指定 Dispatcher 的访问权。</summary>
    bool CheckAccess(Dispatcher dispatcher);

    /// <summary>判断指定 Dispatcher 是否已开始或完成关闭。</summary>
    bool IsShutdown(Dispatcher dispatcher);

    /// <summary>在指定 Dispatcher 上执行同步操作。</summary>
    Task InvokeAsync(Dispatcher dispatcher, Action action, DispatcherPriority priority = DispatcherPriority.Normal);

    /// <summary>在指定 Dispatcher 上执行同步操作并返回结果。</summary>
    Task<T> InvokeAsync<T>(Dispatcher dispatcher, Func<T> action, DispatcherPriority priority = DispatcherPriority.Normal);

    /// <summary>在指定 Dispatcher 上执行异步操作并返回结果。</summary>
    Task<T> InvokeAsync<T>(Dispatcher dispatcher, Func<Task<T>> action, DispatcherPriority priority = DispatcherPriority.Normal);
}
