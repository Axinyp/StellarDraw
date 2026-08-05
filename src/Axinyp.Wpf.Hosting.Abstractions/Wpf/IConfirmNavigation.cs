namespace Axinyp.Wpf.Hosting.Abstractions.Wpf;

/// <summary>激活视图模型可选实现的导航离开确认钩子。</summary>
public interface IConfirmNavigation
{
    /// <summary>
    /// 在区域离开当前内容前请求确认。
    /// </summary>
    /// <param name="context">本次导航上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask<bool> ConfirmNavigationAsync(NavigationContext context, CancellationToken cancellationToken = default);
}
