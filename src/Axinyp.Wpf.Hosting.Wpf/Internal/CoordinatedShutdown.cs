using System.ComponentModel;
using System.Windows;
using Axinyp.Wpf.Hosting.Extensions;
using Microsoft.Extensions.Hosting;

namespace Axinyp.Wpf.Hosting.Wpf.Internal;

/// <summary>
/// Shell 窗口的协调停机状态机：
/// Idle --首次 Closing--&gt; Stopping --成功--&gt; Completed（真正关闭）
///                                   --失败--&gt; Faulted（窗口保持打开；下一次关闭强制退出）。
/// </summary>
internal sealed class CoordinatedShutdown(
    ILogger logger,
    IHost host,
    Window shell,
    WpfApplicationLifecycleOptions options)
{
    private enum ShutdownState
    {
        Idle,
        Stopping,
        Completed,
        Faulted,
    }

    private ShutdownState _state = ShutdownState.Idle;
    private bool _hostDisposed;

    public async void OnShellClosing(object? sender, CancelEventArgs e)
    {
        try
        {
            switch (_state)
            {
                case ShutdownState.Completed:
                    return;

                case ShutdownState.Stopping:
                    e.Cancel = true;
                    return;

                case ShutdownState.Faulted:
                    // 上一次优雅停机已失败：不再重复可能半停的 StopAsync，
                    // 尽力释放容器后放行关闭，保证用户始终能退出应用。
                    ForceCompleteAfterFault();
                    return;
            }

            e.Cancel = true;
            _state = ShutdownState.Stopping;
            logger.LogInformation(WpfHostingEventIds.ApplicationShutdownStarting, "协调停机开始。");

            if (options.BeforeHostStopAsync is not null)
                await options.BeforeHostStopAsync();

            await host.StopModularHostAsync();
            DisposeHostOnce();

            _state = ShutdownState.Completed;
            logger.LogInformation(WpfHostingEventIds.ApplicationShutdownCompleted, "协调停机完成。");

            // 若上面的 await 全部同步完成，此刻仍处于原 Closing 事件的派发栈内，
            // WPF 禁止在窗口关闭序列中再次调用 Close（VerifyNotClosing）。
            // 统一经 Dispatcher 调度重新关闭，保证脱离当前 Closing 派发。
            _ = shell.Dispatcher.BeginInvoke(CloseShellSafely);
        }
        catch (Exception exception)
        {
            _state = ShutdownState.Faulted;
            logger.LogError(WpfHostingEventIds.ApplicationShutdownFailed, exception,
                "协调停机失败；下一次关闭请求将强制退出。");
            if (options.ShowFailureDialogs)
                MessageBox.Show(options.ShutdownFailureMessage, options.FailureCaption,
                    MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>兜底：任何绕过协调停机的退出路径（如 Application.Shutdown）至少释放容器。</summary>
    public void OnApplicationExit(object? sender, ExitEventArgs e) => DisposeHostOnce();

    private void CloseShellSafely()
    {
        try
        {
            shell.Close();
        }
        catch (Exception exception)
        {
            logger.LogError(WpfHostingEventIds.ApplicationShutdownFailed, exception, "停机完成后的窗口关闭失败。");
        }
    }

    private void ForceCompleteAfterFault()
    {
        _state = ShutdownState.Completed;
        try
        {
            DisposeHostOnce();
        }
        catch (Exception exception)
        {
            logger.LogError(WpfHostingEventIds.ApplicationShutdownFailed, exception, "强制退出时释放宿主失败。");
        }
    }

    private void DisposeHostOnce()
    {
        if (_hostDisposed) return;
        _hostDisposed = true;
        host.Dispose();
    }
}
