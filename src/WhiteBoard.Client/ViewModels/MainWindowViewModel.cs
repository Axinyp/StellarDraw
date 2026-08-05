using CommunityToolkit.Mvvm.ComponentModel;

namespace WhiteBoard.Client.ViewModels;

/// <summary>
/// WhiteBoard.Client 的主窗口状态。
/// 它只承载 Shell 的安全初始化状态，不保存导航参数、异常实例或 legacy 插件状态。
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "StellarDraw · Client — Axinyp Host 已接入";

    [ObservableProperty]
    private bool _isInitializing = true;

    [ObservableProperty]
    private bool _hasShellError;

    [ObservableProperty]
    private string _shellMessage = "正在初始化 Shell…";

    [ObservableProperty]
    private string? _shellDiagnosticCode;

    /// <summary>
    /// 更新 Shell 的普通状态，不改变已记录的导航失败信息。
    /// </summary>
    /// <param name="message">已脱敏的用户可见状态。</param>
    public void ReportShellStatus(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!HasShellError)
            ShellMessage = message;
    }

    /// <summary>
    /// 记录 Shell 初始化完成状态。
    /// </summary>
    /// <param name="message">已本地化的成功状态。</param>
    public void ReportShellReady(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        IsInitializing = false;
        HasShellError = false;
        ShellDiagnosticCode = null;
        ShellMessage = message;
    }

    /// <summary>
    /// 记录可安全展示的 Shell 失败结果。
    /// </summary>
    /// <param name="message">不含原始异常或敏感参数的失败说明。</param>
    /// <param name="diagnosticCode">可用于关联日志的稳定诊断代码。</param>
    public void ReportShellFailure(string message, string? diagnosticCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        IsInitializing = false;
        HasShellError = true;
        ShellMessage = message;
        ShellDiagnosticCode = diagnosticCode;
    }
}
