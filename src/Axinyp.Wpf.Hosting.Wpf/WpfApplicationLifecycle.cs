using System.Windows;
using Axinyp.Wpf.Hosting.Extensions;
using Axinyp.Wpf.Hosting.Wpf.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axinyp.Wpf.Hosting.Wpf;

/// <summary>
/// WPF <see cref="Application"/> 与模块化宿主的启停编排。
/// <para>
/// WPF 的 <c>OnStartup</c>/<c>OnExit</c> 都是同步契约：<c>async void</c> 覆写在第一个
/// <c>await</c> 处即向框架返回，异常会逃逸成 Dispatcher 未处理异常，退出路径的异步续体
/// 可能在进程结束前被截断。本类型把这两个已知陷阱封装为一次调用：
/// </para>
/// <list type="bullet">
/// <item>启动失败：记录 Critical 日志、尽力停止并释放宿主、按配置提示用户，然后以退出码 -1 关闭，不重抛；</item>
/// <item>协调停机：首次窗口关闭请求先取消，异步完成「业务收尾 → 宿主停止 → 释放」后再真正关闭；
/// 停机失败保持窗口打开并提示，下一次关闭请求强制退出；</item>
/// <item><see cref="Application.Exit"/> 兜底释放宿主，防止任何绕过协调停机的退出路径泄漏容器。</item>
/// </list>
/// 消费方在 <c>OnStartup</c> 中调用 <see cref="RunAsync"/> 即可，无需再覆写 <c>OnExit</c>。
/// <c>RunAsync</c> 承诺不抛出异常，因此从 <c>async void OnStartup</c> 调用是安全的。
/// </summary>
public static class WpfApplicationLifecycle
{
    /// <summary>构建、启动宿主并显示 Shell；失败与停机路径均已按上述策略处理。</summary>
    public static async Task RunAsync(Application application, WpfApplicationLifecycleOptions options)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(options);

        IHost? host = null;
        try
        {
            host = options.HostFactory();
            await host.StartModularHostAsync();

            var shell = await options.ShellFactory(host);
            application.MainWindow = shell;

            var coordinator = new CoordinatedShutdown(GetLogger(host), host, shell, options);
            shell.Closing += coordinator.OnShellClosing;
            application.Exit += coordinator.OnApplicationExit;

            if (options.AfterShellShownAsync is not null)
                await RunPostStartupAsync(host, shell, options);
        }
        catch (Exception exception)
        {
            var logger = GetLogger(host);
            logger.LogCritical(WpfHostingEventIds.ApplicationStartupFailed, exception, "WPF 应用宿主启动失败。");

            if (host is not null)
            {
                try
                {
                    await host.StopModularHostAsync();
                }
                catch (Exception stopException)
                {
                    logger.LogError(WpfHostingEventIds.ApplicationStartupCleanupFailed, stopException,
                        "启动失败后的宿主停止清理失败。");
                }
                finally
                {
                    host.Dispose();
                }
            }

            if (options.ShowFailureDialogs)
                MessageBox.Show(options.StartupFailureMessage, options.FailureCaption,
                    MessageBoxButton.OK, MessageBoxImage.Error);

            (options.ShutdownAction ?? application.Shutdown)(-1);
        }
    }

    private static async Task RunPostStartupAsync(IHost host, Window shell, WpfApplicationLifecycleOptions options)
    {
        try
        {
            await options.AfterShellShownAsync!(host, shell);
        }
        catch (Exception exception)
        {
            GetLogger(host).LogError(WpfHostingEventIds.ApplicationPostStartupFailed, exception,
                "Shell 显示后的后续初始化失败；应用继续运行。");
            if (options.ShowFailureDialogs)
                MessageBox.Show(options.StartupFailureMessage, options.FailureCaption,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static ILogger GetLogger(IHost? host)
    {
        try
        {
            return host?.Services.GetService<ILoggerFactory>()
                       ?.CreateLogger(typeof(WpfApplicationLifecycle).FullName!)
                   ?? NullLogger.Instance;
        }
        catch (ObjectDisposedException)
        {
            return NullLogger.Instance;
        }
    }
}
