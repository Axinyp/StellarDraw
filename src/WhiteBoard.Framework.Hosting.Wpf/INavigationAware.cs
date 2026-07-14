namespace WhiteBoard.Framework.Hosting.Wpf;

/// <summary>
/// 导航感知 ViewModel。由框架在区域内容切换前后调用，用于接收参数、刷新状态或完成离开收尾。
/// 任何异常都会使本次导航失败，区域保留原有内容。
/// </summary>
public interface INavigationAware
{
    Task OnNavigatedToAsync(NavigationContext context, CancellationToken cancellationToken = default);

    Task OnNavigatedFromAsync(NavigationContext context, CancellationToken cancellationToken = default);
}
