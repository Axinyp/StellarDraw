using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

namespace Axinyp.Wpf.Hosting.Wpf.Internal;

internal sealed class DialogService(ViewCompositionFactory compositionFactory, IUiDispatcher uiDispatcher) : IDialogService
{
    public Task<IDialogResult> ShowDialogAsync<TViewModel>(Window owner, DialogParameters? parameters = null,
        CancellationToken cancellationToken = default) where TViewModel : class
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<IDialogResult>(cancellationToken);
        return uiDispatcher.InvokeAsync(owner.Dispatcher, () => Show(typeof(TViewModel), owner, parameters ?? []));
    }

    private IDialogResult Show(Type viewModelType, Window owner, DialogParameters parameters)
    {
        using var handle = compositionFactory.Create(viewModelType);
        if (handle.View is not Window window)
            throw new InvalidOperationException($"View '{handle.View.GetType().FullName}' 不是 Window，不能显示为 Dialog。");
        window.Owner = owner;
        IDialogResult result = DialogResult.Cancel();
        if (handle.ViewModel is IDialogAware aware)
        {
            IDialogResult? pendingClose = null;
            void ApplyClose(IDialogResult value)
            {
                if (!aware.CanCloseDialog()) return;
                result = value;
                if (!window.IsVisible)
                {
                    pendingClose = value;
                    return;
                }
                window.DialogResult = value.Result;
                window.Close();
            }

            var active = 1;
            EventHandler<IDialogResult>? close = (_, value) =>
            {
                if (Volatile.Read(ref active) == 0)
                    return;

                try
                {
                    ObserveCloseRequest(uiDispatcher.InvokeAsync(window.Dispatcher, () =>
                    {
                        if (Volatile.Read(ref active) == 1)
                            ApplyClose(value);
                    }));
                }
                catch (InvalidOperationException) when (uiDispatcher.IsShutdown(window.Dispatcher))
                {
                    Trace.TraceError("对话框关闭请求无法在已关闭的 Dispatcher 上执行。");
                }
            };
            RoutedEventHandler? loaded = (_, _) =>
            {
                if (pendingClose is null)
                    return;

                try
                {
                    ApplyClose(pendingClose);
                }
                catch (Exception exception)
                {
                    // Loaded 是 WPF 事件处理器，异常会直接冒泡成 Dispatcher 未处理异常；
                    // 延迟关闭失败时保持对话框打开并记录，等待用户或 VM 的下一次关闭请求。
                    Trace.TraceError("对话框延迟关闭请求执行失败：{0}", exception);
                }
            };
            CancelEventHandler? closing = (_, eventArgs) =>
            {
                try
                {
                    if (!aware.CanCloseDialog()) eventArgs.Cancel = true;
                }
                catch (Exception exception)
                {
                    // CanCloseDialog 抛异常说明 VM 守卫自身已损坏：
                    // 若在此阻止关闭，对话框将永久无法关闭；放行关闭并保持 Cancel 结果。
                    Trace.TraceError("CanCloseDialog 抛出异常，按取消结果放行关闭：{0}", exception);
                }
            };
            aware.RequestClose += close;
            window.Loaded += loaded;
            window.Closing += closing;
            try { aware.OnDialogOpened(parameters); window.ShowDialog(); }
            finally
            {
                Volatile.Write(ref active, 0);
                aware.RequestClose -= close;
                window.Loaded -= loaded;
                window.Closing -= closing;
                aware.OnDialogClosed();
            }
        }
        else result = new DialogResult(window.ShowDialog());
        return result;
    }

    private static void ObserveCloseRequest(Task request)
    {
        _ = request.ContinueWith(
            completed => Trace.TraceError("对话框关闭请求调度失败：{0}", completed.Exception),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
