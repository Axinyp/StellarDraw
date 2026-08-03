namespace Axinyp.Wpf.Hosting.Wpf.Internal;

/// <summary>区域导航实现：仅保存轻量 journal，替换内容时立即释放旧 View/VM scope。</summary>
internal sealed class NavigationService(
    RegionManager regionManager,
    ViewCompositionFactory compositionFactory,
    IUiDispatcher uiDispatcher,
    ILogger<NavigationService> logger) : INavigationService
{
    public Task<NavigationResult> NavigateAsync<TViewModel>(
        string regionName,
        object? parameter = null,
        CancellationToken cancellationToken = default)
        where TViewModel : class
        => NavigateAsync(regionName, typeof(TViewModel), parameter, cancellationToken);

    public Task<NavigationResult> NavigateAsync(
        string regionName,
        Type viewModelType,
        object? parameter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);
        var region = regionManager.GetHost(regionName);
        return OnRegionDispatcherAsync(region, NavigationKind.Forward, viewModelType, async () =>
        {
            if (cancellationToken.IsCancellationRequested)
                return LogFailure(region, NavigationKind.Forward, viewModelType,
                    NavigationFailureKind.Cancelled, "navigation.cancelled", "导航已取消。");

            ViewHandle? next = null;
            var stage = NavigationStage.Confirmation;
            try
            {
                if (!await CanLeaveAsync(region, viewModelType, parameter, NavigationKind.Forward, cancellationToken)
                        .ConfigureAwait(true))
                    return LogCancelled(region, NavigationKind.Forward, viewModelType);

                stage = NavigationStage.Composition;
                next = compositionFactory.Create(viewModelType);
                if (next.ViewModel is INavigationAware entering)
                {
                    stage = NavigationStage.Entering;
                    await entering.OnNavigatedToAsync(new NavigationContext(region.Name, region.ActiveViewModel,
                        viewModelType, parameter, NavigationKind.Forward), cancellationToken).ConfigureAwait(true);
                }

                if (region.ActiveViewModel is INavigationAware leaving)
                {
                    stage = NavigationStage.Leaving;
                    await leaving.OnNavigatedFromAsync(new NavigationContext(region.Name, region.ActiveViewModel,
                        viewModelType, parameter, NavigationKind.Forward), cancellationToken).ConfigureAwait(true);
                }

                if (region.ActiveViewModelType is not null)
                    region.PushBack(new NavigationJournalEntry(region.ActiveViewModelType, region.ActiveParameter));
                var cleanupException = region.Replace(next, parameter);
                LogCleanupFailure(region, NavigationKind.Forward, viewModelType, cleanupException);
                logger.LogInformation(WpfHostingEventIds.NavigationSucceeded,
                    "区域 {RegionName} 已完成 {NavigationKind} 导航至 {TargetViewModelType}。",
                    region.Name, NavigationKind.Forward, viewModelType.FullName);
                return NavigationResult.Success;
            }
            catch (Exception exception)
            {
                next?.Dispose();
                return LogFailure(region, NavigationKind.Forward, viewModelType,
                    ToFailureKind(stage), ToDiagnosticCode(stage), "导航未能完成。", exception);
            }
        });
    }

    public bool CanGoBack(string regionName) => regionManager.GetHost(regionName).CanGoBack;

    public Task<NavigationResult> GoBackAsync(string regionName, CancellationToken cancellationToken = default)
    {
        var region = regionManager.GetHost(regionName);
        var targetType = region.CanGoBack ? region.PeekBack().ViewModelType : null;
        return OnRegionDispatcherAsync(region, NavigationKind.Back, targetType, async () =>
        {
            if (!region.CanGoBack)
            {
                logger.LogInformation(WpfHostingEventIds.NavigationBackUnavailable,
                    "区域 {RegionName} 没有可返回的导航记录。", region.Name);
                return NavigationResult.Failed("当前区域没有可返回的导航记录。",
                    NavigationFailureKind.NoBackStack, "navigation.back-unavailable",
                    eventId: WpfHostingEventIds.NavigationBackUnavailable.Id);
            }

            if (cancellationToken.IsCancellationRequested)
                return LogFailure(region, NavigationKind.Back, targetType,
                    NavigationFailureKind.Cancelled, "navigation.cancelled", "导航已取消。");

            var target = region.PeekBack();
            ViewHandle? next = null;
            var stage = NavigationStage.Confirmation;
            try
            {
                if (!await CanLeaveAsync(region, target.ViewModelType, target.Parameter, NavigationKind.Back, cancellationToken)
                        .ConfigureAwait(true))
                    return LogCancelled(region, NavigationKind.Back, target.ViewModelType);

                stage = NavigationStage.Composition;
                next = compositionFactory.Create(target.ViewModelType);
                if (next.ViewModel is INavigationAware entering)
                {
                    stage = NavigationStage.Entering;
                    await entering.OnNavigatedToAsync(new NavigationContext(region.Name, region.ActiveViewModel,
                        target.ViewModelType, target.Parameter, NavigationKind.Back), cancellationToken).ConfigureAwait(true);
                }

                if (region.ActiveViewModel is INavigationAware leaving)
                {
                    stage = NavigationStage.Leaving;
                    await leaving.OnNavigatedFromAsync(new NavigationContext(region.Name, region.ActiveViewModel,
                        target.ViewModelType, target.Parameter, NavigationKind.Back), cancellationToken).ConfigureAwait(true);
                }

                region.PopBack();
                var cleanupException = region.Replace(next, target.Parameter);
                LogCleanupFailure(region, NavigationKind.Back, target.ViewModelType, cleanupException);
                logger.LogInformation(WpfHostingEventIds.NavigationSucceeded,
                    "区域 {RegionName} 已完成 {NavigationKind} 导航至 {TargetViewModelType}。",
                    region.Name, NavigationKind.Back, target.ViewModelType.FullName);
                return NavigationResult.Success;
            }
            catch (Exception exception)
            {
                next?.Dispose();
                return LogFailure(region, NavigationKind.Back, target.ViewModelType,
                    ToFailureKind(stage), ToDiagnosticCode(stage), "导航未能完成。", exception);
            }
        });
    }

    private async Task<NavigationResult> OnRegionDispatcherAsync(
        RegionHost region,
        NavigationKind kind,
        Type? targetType,
        Func<Task<NavigationResult>> action)
    {
        try
        {
            return await uiDispatcher.InvokeAsync(region.Dispatcher, action).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return LogFailure(region, kind, targetType,
                NavigationFailureKind.DispatcherUnavailable, "navigation.dispatcher-unavailable",
                "导航无法在 UI 线程执行。", exception);
        }
    }

    private void LogCleanupFailure(
        RegionHost region,
        NavigationKind kind,
        Type targetType,
        Exception? exception)
    {
        if (exception is null) return;

        logger.LogError(WpfHostingEventIds.NavigationCleanupFailed, exception,
            "区域 {RegionName} 的 {NavigationKind} 导航已提交，但旧区域内容回收失败。目标 ViewModel 为 {TargetViewModelType}。",
            region.Name, kind, targetType.FullName);
    }

    private NavigationResult LogCancelled(RegionHost region, NavigationKind kind, Type? targetType)
    {
        logger.LogInformation(WpfHostingEventIds.NavigationCancelled,
            "区域 {RegionName} 的 {NavigationKind} 导航被当前 ViewModel 取消。目标 ViewModel 为 {TargetViewModelType}。",
            region.Name, kind, targetType?.FullName);
        return NavigationResult.CancelledByViewModel;
    }

    private NavigationResult LogFailure(
        RegionHost region,
        NavigationKind kind,
        Type? targetType,
        NavigationFailureKind failureKind,
        string diagnosticCode,
        string message,
        Exception? exception = null)
    {
        logger.LogError(WpfHostingEventIds.NavigationFailed, exception,
            "区域 {RegionName} 的 {NavigationKind} 导航失败。目标 ViewModel 为 {TargetViewModelType}。FailureKind={FailureKind}; DiagnosticCode={DiagnosticCode}",
            region.Name, kind, targetType?.FullName, failureKind, diagnosticCode);
        return NavigationResult.Failed(message, failureKind, diagnosticCode, exception?.GetType().FullName,
            WpfHostingEventIds.NavigationFailed.Id);
    }

    private static async ValueTask<bool> CanLeaveAsync(
        RegionHost region,
        Type targetType,
        object? parameter,
        NavigationKind kind,
        CancellationToken cancellationToken)
    {
        if (region.ActiveViewModel is not IConfirmNavigation confirmation) return true;
        var context = new NavigationContext(region.Name, region.ActiveViewModel, targetType, parameter, kind);
        return await confirmation.ConfirmNavigationAsync(context, cancellationToken).ConfigureAwait(true);
    }

    private static NavigationFailureKind ToFailureKind(NavigationStage stage) => stage switch
    {
        NavigationStage.Composition => NavigationFailureKind.ViewComposition,
        NavigationStage.Entering => NavigationFailureKind.EnteringLifecycle,
        NavigationStage.Confirmation or NavigationStage.Leaving => NavigationFailureKind.LeavingLifecycle,
        _ => NavigationFailureKind.Unexpected
    };

    private static string ToDiagnosticCode(NavigationStage stage) => stage switch
    {
        NavigationStage.Composition => "navigation.view-composition-failed",
        NavigationStage.Entering => "navigation.entering-lifecycle-failed",
        NavigationStage.Confirmation or NavigationStage.Leaving => "navigation.leaving-lifecycle-failed",
        _ => "navigation.unexpected-failure"
    };

    private enum NavigationStage
    {
        Confirmation,
        Composition,
        Entering,
        Leaving
    }
}
