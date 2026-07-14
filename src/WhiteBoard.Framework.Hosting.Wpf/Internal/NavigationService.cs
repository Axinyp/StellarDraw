namespace WhiteBoard.Framework.Hosting.Wpf.Internal;

/// <summary>区域导航实现：仅保存轻量 journal，替换内容时立即释放旧 View/VM scope。</summary>
internal sealed class NavigationService(RegionManager regionManager, ViewCompositionFactory compositionFactory)
    : INavigationService
{
    public Task<NavigationResult> NavigateAsync<TViewModel>(string regionName, object? parameter = null,
        CancellationToken cancellationToken = default) where TViewModel : class
        => NavigateAsync(regionName, typeof(TViewModel), parameter, cancellationToken);

    public Task<NavigationResult> NavigateAsync(string regionName, Type viewModelType, object? parameter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);
        var region = regionManager.GetHost(regionName);
        return OnRegionDispatcherAsync(region, async () =>
        {
            if (cancellationToken.IsCancellationRequested)
                return NavigationResult.Failed("导航已取消。");
            if (!await CanLeaveAsync(region, viewModelType, parameter, NavigationKind.Forward, cancellationToken)
                    .ConfigureAwait(true))
                return NavigationResult.CancelledByViewModel;

            ViewHandle? next = null;
            try
            {
                next = compositionFactory.Create(viewModelType);
                if (next.ViewModel is INavigationAware entering)
                    await entering.OnNavigatedToAsync(new NavigationContext(region.Name, region.ActiveViewModel,
                        viewModelType, parameter, NavigationKind.Forward), cancellationToken).ConfigureAwait(true);
                if (region.ActiveViewModel is INavigationAware leaving)
                    await leaving.OnNavigatedFromAsync(new NavigationContext(region.Name, region.ActiveViewModel,
                        viewModelType, parameter, NavigationKind.Forward), cancellationToken).ConfigureAwait(true);

                if (region.ActiveViewModelType is not null)
                    region.BackStack.Push(new NavigationJournalEntry(region.ActiveViewModelType, region.ActiveParameter));
                region.Replace(next, parameter);
                return NavigationResult.Success;
            }
            catch (Exception ex)
            {
                next?.Dispose();
                return NavigationResult.Failed(ex.Message);
            }
        });
    }

    public bool CanGoBack(string regionName)
    {
        var region = regionManager.GetHost(regionName);
        if (!region.Dispatcher.CheckAccess())
            return region.Dispatcher.Invoke(() => region.BackStack.Count > 0);
        return region.BackStack.Count > 0;
    }

    public Task<NavigationResult> GoBackAsync(string regionName, CancellationToken cancellationToken = default)
    {
        var region = regionManager.GetHost(regionName);
        return OnRegionDispatcherAsync(region, async () =>
        {
            if (region.BackStack.Count == 0)
                return NavigationResult.Failed("当前区域没有可返回的导航记录。");
            if (cancellationToken.IsCancellationRequested)
                return NavigationResult.Failed("导航已取消。");

            var target = region.BackStack.Peek();
            if (!await CanLeaveAsync(region, target.ViewModelType, target.Parameter, NavigationKind.Back, cancellationToken)
                    .ConfigureAwait(true))
                return NavigationResult.CancelledByViewModel;

            ViewHandle? next = null;
            try
            {
                next = compositionFactory.Create(target.ViewModelType);
                if (next.ViewModel is INavigationAware entering)
                    await entering.OnNavigatedToAsync(new NavigationContext(region.Name, region.ActiveViewModel,
                        target.ViewModelType, target.Parameter, NavigationKind.Back), cancellationToken).ConfigureAwait(true);
                if (region.ActiveViewModel is INavigationAware leaving)
                    await leaving.OnNavigatedFromAsync(new NavigationContext(region.Name, region.ActiveViewModel,
                        target.ViewModelType, target.Parameter, NavigationKind.Back), cancellationToken).ConfigureAwait(true);

                region.BackStack.Pop();
                region.Replace(next, target.Parameter);
                return NavigationResult.Success;
            }
            catch (Exception ex)
            {
                next?.Dispose();
                return NavigationResult.Failed(ex.Message);
            }
        });
    }

    private static async ValueTask<bool> CanLeaveAsync(RegionHost region, Type targetType, object? parameter,
        NavigationKind kind, CancellationToken cancellationToken)
    {
        if (region.ActiveViewModel is not IConfirmNavigation confirmation) return true;
        var context = new NavigationContext(region.Name, region.ActiveViewModel, targetType, parameter, kind);
        return await confirmation.ConfirmNavigationAsync(context, cancellationToken).ConfigureAwait(true);
    }

    private static Task<T> OnRegionDispatcherAsync<T>(RegionHost region, Func<Task<T>> action)
    {
        if (region.Dispatcher.HasShutdownStarted || region.Dispatcher.HasShutdownFinished)
            return Task.FromException<T>(new InvalidOperationException($"区域 '{region.Name}' 的 Dispatcher 正在关闭。"));
        if (region.Dispatcher.CheckAccess()) return action();
        return region.Dispatcher.InvokeAsync(action).Task.Unwrap();
    }
}
