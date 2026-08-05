namespace Axinyp.Wpf.Hosting.Abstractions.Wpf;

/// <summary>
/// 导航感知 ViewModel。由框架在区域内容切换前后调用，用于接收参数、刷新状态或完成离开收尾。
/// 任何异常都会使本次导航失败，区域保留原有内容。
/// </summary>
public interface INavigationAware
{
    /// <summary>
    /// 导航进入当前 ViewModel 后调用。
    /// </summary>
    /// <param name="context">本次导航上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task OnNavigatedToAsync(NavigationContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 导航离开当前 ViewModel 前调用。
    /// </summary>
    /// <param name="context">本次导航上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task OnNavigatedFromAsync(NavigationContext context, CancellationToken cancellationToken = default);
}
