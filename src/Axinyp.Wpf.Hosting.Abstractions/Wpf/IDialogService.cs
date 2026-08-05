using System.Windows;

namespace Axinyp.Wpf.Hosting.Abstractions.Wpf;

/// <summary>
/// 基于 ViewModel 的对话框服务。
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// 以模态方式显示指定 ViewModel 对应的对话框。
    /// </summary>
    /// <typeparam name="TViewModel">对话框 ViewModel 类型。</typeparam>
    /// <param name="owner">拥有者窗口。</param>
    /// <param name="parameters">打开参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IDialogResult> ShowDialogAsync<TViewModel>(Window owner, DialogParameters? parameters = null,
        CancellationToken cancellationToken = default) where TViewModel : class;
}
