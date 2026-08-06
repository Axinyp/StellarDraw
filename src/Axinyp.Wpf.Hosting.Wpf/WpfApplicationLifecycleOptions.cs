using System.Windows;
using Microsoft.Extensions.Hosting;

namespace Axinyp.Wpf.Hosting.Wpf;

/// <summary>
/// <see cref="WpfApplicationLifecycle.RunAsync"/> 的启停编排配置。
/// </summary>
public sealed class WpfApplicationLifecycleOptions
{
    /// <summary>构建模块化宿主（含全部服务与模块注册）。仅调用一次。</summary>
    public required Func<IHost> HostFactory { get; init; }

    /// <summary>
    /// 宿主启动成功后创建并显示 Shell 主窗口（通常经 IWindowManager）。
    /// 返回的窗口会被挂接协调停机：首次关闭请求先取消、异步完成宿主停止后再真正关闭。
    /// </summary>
    public required Func<IHost, Task<Window>> ShellFactory { get; init; }

    /// <summary>
    /// Shell 显示完成后的后续初始化（如加载业务插件）。
    /// 异常会记录并按 <see cref="ShowFailureDialogs"/> 提示，但不会终止应用——
    /// 后续初始化属于业务接入层，其失败不应被混淆为宿主启动失败。
    /// </summary>
    public Func<IHost, Window, Task>? AfterShellShownAsync { get; init; }

    /// <summary>
    /// 协调停机时、停止宿主之前执行的业务收尾（如插件卸载、视图脱离）。
    /// 抛出的异常会使本次停机失败并保持窗口打开；下一次关闭请求将强制退出。
    /// </summary>
    public Func<Task>? BeforeHostStopAsync { get; init; }

    /// <summary>失败对话框标题。</summary>
    public string FailureCaption { get; init; } = "应用错误";

    /// <summary>宿主启动失败时的用户提示文本；不得包含敏感信息。</summary>
    public string StartupFailureMessage { get; init; } = "应用无法启动，请查看应用日志后重试。";

    /// <summary>协调停机失败时的用户提示文本。</summary>
    public string ShutdownFailureMessage { get; init; } = "应用关闭未能完成，请查看应用日志；再次关闭窗口将强制退出。";

    /// <summary>是否弹出失败提示对话框。无人值守或测试场景可关闭，仅保留日志。</summary>
    public bool ShowFailureDialogs { get; init; } = true;

    /// <summary>
    /// 启动失败时的退出动作，默认 <c>application.Shutdown(exitCode)</c>。
    /// 测试宿主共享 Application 时可替换，避免真正关闭消息循环。
    /// </summary>
    public Action<int>? ShutdownAction { get; init; }
}
