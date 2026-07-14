namespace WhiteBoard.Framework.Hosting.Wpf;

/// <summary>激活视图模型可选实现的导航离开确认钩子。</summary>
public interface IConfirmNavigation
{
    ValueTask<bool> ConfirmNavigationAsync(NavigationContext context, CancellationToken cancellationToken = default);
}
