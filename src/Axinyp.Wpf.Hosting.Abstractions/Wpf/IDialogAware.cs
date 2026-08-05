namespace Axinyp.Wpf.Hosting.Abstractions.Wpf;

/// <summary>
/// 对话框打开时传递给 ViewModel 的参数字典。
/// </summary>
public sealed class DialogParameters : Dictionary<string, object?>
{
    /// <summary>
    /// 创建一个区分大小写的参数字典。
    /// </summary>
    public DialogParameters() : base(StringComparer.Ordinal) { }
}

/// <summary>
/// 对话框感知 ViewModel。
/// 由框架在对话框打开、关闭以及请求关闭时调用。
/// </summary>
public interface IDialogAware
{
    /// <summary>
    /// ViewModel 请求宿主关闭对话框时触发。
    /// </summary>
    event EventHandler<IDialogResult>? RequestClose;

    /// <summary>
    /// 对话框打开后调用。
    /// </summary>
    /// <param name="parameters">打开对话框时传入的参数。</param>
    void OnDialogOpened(DialogParameters parameters);

    /// <summary>
    /// 判断对话框当前是否允许关闭。
    /// </summary>
    bool CanCloseDialog();

    /// <summary>
    /// 对话框关闭后调用。
    /// </summary>
    void OnDialogClosed();
}
