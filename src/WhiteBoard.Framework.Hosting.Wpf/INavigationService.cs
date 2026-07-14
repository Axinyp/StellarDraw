namespace WhiteBoard.Framework.Hosting.Wpf;

/// <summary>基于 ViewModel 的区域导航服务。</summary>
public interface INavigationService
{
    Task<NavigationResult> NavigateAsync<TViewModel>(string regionName, object? parameter = null,
        CancellationToken cancellationToken = default) where TViewModel : class;

    Task<NavigationResult> NavigateAsync(string regionName, Type viewModelType, object? parameter = null,
        CancellationToken cancellationToken = default);

    bool CanGoBack(string regionName);

    Task<NavigationResult> GoBackAsync(string regionName, CancellationToken cancellationToken = default);
}
