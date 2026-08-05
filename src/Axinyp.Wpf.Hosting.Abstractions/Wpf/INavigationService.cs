namespace Axinyp.Wpf.Hosting.Abstractions.Wpf;

/// <summary>基于 ViewModel 的区域导航服务。</summary>
public interface INavigationService
{
    /// <summary>
    /// 导航到指定区域中的目标 ViewModel 类型。
    /// </summary>
    /// <typeparam name="TViewModel">目标 ViewModel 类型。</typeparam>
    /// <param name="regionName">区域名。</param>
    /// <param name="parameter">导航参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<NavigationResult> NavigateAsync<TViewModel>(string regionName, object? parameter = null,
        CancellationToken cancellationToken = default) where TViewModel : class;

    /// <summary>
    /// 导航到指定区域中的目标 ViewModel 运行时类型。
    /// </summary>
    /// <param name="regionName">区域名。</param>
    /// <param name="viewModelType">目标 ViewModel 类型。</param>
    /// <param name="parameter">导航参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<NavigationResult> NavigateAsync(string regionName, Type viewModelType, object? parameter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断指定区域是否存在可回退历史。
    /// </summary>
    /// <param name="regionName">区域名。</param>
    bool CanGoBack(string regionName);

    /// <summary>
    /// 回退到指定区域上一条导航历史。
    /// </summary>
    /// <param name="regionName">区域名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<NavigationResult> GoBackAsync(string regionName, CancellationToken cancellationToken = default);
}
