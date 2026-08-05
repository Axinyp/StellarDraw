namespace Axinyp.Wpf.Hosting.Wpf.Tests;

public sealed class NavigationServiceTests
{
    [Fact]
    public async Task NavigateAndGoBack_ReplacesViewClearsOldDataContextAndRecreatesJournalEntry()
    {
        await Sta.RunAsync(async () =>
        {
            var services = CreateServices();
            using var provider = services.BuildServiceProvider();
            var regions = provider.GetRequiredService<IRegionManager>();
            var navigation = provider.GetRequiredService<INavigationService>();
            var host = new ContentControl();
            using var registration = regions.RegisterRegion("Main", host);

            Assert.True((await navigation.NavigateAsync<FirstViewModel>("Main")).Succeeded);
            var first = Assert.IsType<FirstView>(host.Content);
            var firstViewModel = Assert.IsType<FirstViewModel>(first.DataContext);

            const string parameter = "lesson-42";
            Assert.True((await navigation.NavigateAsync<SecondViewModel>("Main", parameter)).Succeeded);
            var second = Assert.IsType<SecondView>(host.Content);
            var secondViewModel = Assert.IsType<SecondViewModel>(second.DataContext);
            Assert.Equal(parameter, secondViewModel.Parameter);
            Assert.Null(first.DataContext);
            Assert.True(firstViewModel.Disposed);

            Assert.True((await navigation.GoBackAsync("Main")).Succeeded);
            var returned = Assert.IsType<FirstView>(host.Content);
            Assert.NotSame(first, returned);
            Assert.IsType<FirstViewModel>(returned.DataContext);
            Assert.Null(second.DataContext);
        });
    }

    [Fact]
    public async Task Navigate_WhenCurrentViewModelRejectsLeave_KeepsCurrentRegionContent()
    {
        await Sta.RunAsync(async () =>
        {
            var loggerProvider = new RecordingLoggerProvider();
            var services = CreateServices();
            services.AddSingleton<GuardedViewModel>();
            services.AddLogging(builder => builder.AddProvider(loggerProvider));
            using var provider = services.BuildServiceProvider();
            var regions = provider.GetRequiredService<IRegionManager>();
            var navigation = provider.GetRequiredService<INavigationService>();
            var host = new ContentControl();
            using var registration = regions.RegisterRegion("Main", host);

            Assert.True((await navigation.NavigateAsync<GuardedViewModel>("Main")).Succeeded);
            var guarded = Assert.IsType<GuardedView>(host.Content);

            var result = await navigation.NavigateAsync<SecondViewModel>("Main");

            Assert.True(result.Cancelled);
            Assert.Equal(NavigationFailureKind.Cancelled, result.FailureKind);
            Assert.Equal("navigation.cancelled", result.DiagnosticCode);
            Assert.Equal(WpfHostingEventIds.NavigationCancelled.Id, result.EventId);
            Assert.Same(guarded, host.Content);
            var log = Assert.Single(loggerProvider.Entries, entry =>
                entry.EventId == WpfHostingEventIds.NavigationCancelled);
            Assert.Equal(LogLevel.Information, log.LogLevel);
            Assert.Equal("Main", log.Properties["RegionName"]);
            Assert.Equal(typeof(SecondViewModel).FullName, log.Properties["TargetViewModelType"]);
        });
    }

    [Fact]
    public async Task Navigate_WhenEnteringLifecycleFails_ReleasesCandidateScopeAndLogsOriginalException()
    {
        await Sta.RunAsync(async () =>
        {
            var probe = new DisposalProbe();
            var loggerProvider = new RecordingLoggerProvider();
            var services = CreateServices();
            services.AddLogging(builder => builder.AddProvider(loggerProvider));
            services.AddScoped(_ => probe);
            services.AddTransient<FailingEnteringViewModel>();
            services.AddView<FailingEnteringViewModel, FailingEnteringView>();
            using var provider = services.BuildServiceProvider();
            var regions = provider.GetRequiredService<IRegionManager>();
            var navigation = provider.GetRequiredService<INavigationService>();
            using var registration = regions.RegisterRegion("Main", new ContentControl());

            var result = await navigation.NavigateAsync<FailingEnteringViewModel>("Main");

            Assert.False(result.Succeeded);
            Assert.Equal(NavigationFailureKind.EnteringLifecycle, result.FailureKind);
            Assert.Equal("navigation.entering-lifecycle-failed", result.DiagnosticCode);
            Assert.Equal(WpfHostingEventIds.NavigationFailed.Id, result.EventId);
            Assert.Equal(typeof(InvalidOperationException).FullName, result.ExceptionType);
            Assert.True(probe.Disposed);
            var log = Assert.Single(loggerProvider.Entries, entry =>
                entry.EventId == WpfHostingEventIds.NavigationFailed);
            Assert.Equal(LogLevel.Error, log.LogLevel);
            Assert.IsType<InvalidOperationException>(log.Exception);
            Assert.Equal("Main", log.Properties["RegionName"]);
            Assert.Equal(typeof(FailingEnteringViewModel).FullName, log.Properties["TargetViewModelType"]);
            Assert.Equal(NavigationFailureKind.EnteringLifecycle, log.Properties["FailureKind"]);
            Assert.False(log.Properties.ContainsKey("Parameter"));
        });
    }

    [Fact]
    public async Task GoBack_WhenNoJournalEntry_ReturnsStableDiagnosticAndLogs()
    {
        await Sta.RunAsync(async () =>
        {
            var loggerProvider = new RecordingLoggerProvider();
            var services = CreateServices();
            services.AddLogging(builder => builder.AddProvider(loggerProvider));
            using var provider = services.BuildServiceProvider();
            var regions = provider.GetRequiredService<IRegionManager>();
            var navigation = provider.GetRequiredService<INavigationService>();
            using var registration = regions.RegisterRegion("Main", new ContentControl());

            var result = await navigation.GoBackAsync("Main");

            Assert.False(result.Succeeded);
            Assert.Equal(NavigationFailureKind.NoBackStack, result.FailureKind);
            Assert.Equal("navigation.back-unavailable", result.DiagnosticCode);
            Assert.Equal(WpfHostingEventIds.NavigationBackUnavailable.Id, result.EventId);
            var log = Assert.Single(loggerProvider.Entries, entry =>
                entry.EventId == WpfHostingEventIds.NavigationBackUnavailable);
            Assert.Equal(LogLevel.Information, log.LogLevel);
            Assert.Equal("Main", log.Properties["RegionName"]);
        });
    }

    [Fact]
    public async Task NavigateAsync_WhenRegionDispatcherIsUnavailable_ReturnsStableFailureWithoutCreatingCandidateViewModel()
    {
        await Sta.RunAsync(async () =>
        {
            UnavailableViewModel.Reset();
            var loggerProvider = new RecordingLoggerProvider();
            var services = CreateServices();
            services.AddLogging(builder => builder.AddProvider(loggerProvider));
            services.AddSingleton<IUiDispatcher>(new RejectingUiDispatcher(Application.Current.Dispatcher));
            services.AddTransient<UnavailableViewModel>();
            services.AddView<UnavailableViewModel, UnavailableView>();
            using var provider = services.BuildServiceProvider();
            var regions = provider.GetRequiredService<IRegionManager>();
            var navigation = provider.GetRequiredService<INavigationService>();
            using var registration = regions.RegisterRegion("Main", new ContentControl());

            var result = await navigation.NavigateAsync<UnavailableViewModel>("Main");

            Assert.False(result.Succeeded);
            Assert.Equal(NavigationFailureKind.DispatcherUnavailable, result.FailureKind);
            Assert.Equal("navigation.dispatcher-unavailable", result.DiagnosticCode);
            Assert.Equal(WpfHostingEventIds.NavigationFailed.Id, result.EventId);
            Assert.Equal(typeof(InvalidOperationException).FullName, result.ExceptionType);
            Assert.Equal(0, UnavailableViewModel.CreatedCount);
            var log = Assert.Single(loggerProvider.Entries, entry =>
                entry.EventId == WpfHostingEventIds.NavigationFailed);
            Assert.Equal(LogLevel.Error, log.LogLevel);
            Assert.IsType<InvalidOperationException>(log.Exception);
            Assert.False(log.Properties.ContainsKey("Parameter"));
        });
    }

    [Fact]
    public async Task Navigate_WhenPreviousScopeDisposalFails_KeepsCommittedPageAndLogsCleanupFailure()
    {
        await Sta.RunAsync(async () =>
        {
            var loggerProvider = new RecordingLoggerProvider();
            var services = CreateServices();
            services.AddLogging(builder => builder.AddProvider(loggerProvider));
            services.AddTransient<ThrowingDisposeViewModel>();
            services.AddView<ThrowingDisposeViewModel, ThrowingDisposeView>();
            using var provider = services.BuildServiceProvider();
            var regions = provider.GetRequiredService<IRegionManager>();
            var navigation = provider.GetRequiredService<INavigationService>();
            var host = new ContentControl();
            using var registration = regions.RegisterRegion("Main", host);

            Assert.True((await navigation.NavigateAsync<ThrowingDisposeViewModel>("Main")).Succeeded);

            var result = await navigation.NavigateAsync<SecondViewModel>("Main");

            Assert.True(result.Succeeded);
            var second = Assert.IsType<SecondView>(host.Content);
            Assert.IsType<SecondViewModel>(second.DataContext);
            var cleanupLog = Assert.Single(loggerProvider.Entries, entry =>
                entry.EventId == WpfHostingEventIds.NavigationCleanupFailed);
            Assert.Equal(LogLevel.Error, cleanupLog.LogLevel);
            Assert.IsType<InvalidOperationException>(cleanupLog.Exception);
            Assert.Equal("Main", cleanupLog.Properties["RegionName"]);
            Assert.Equal(typeof(SecondViewModel).FullName, cleanupLog.Properties["TargetViewModelType"]);
        });
    }

    [Fact]
    public async Task NavigateAndGoBack_WhenForwardLifecycleIsPending_SerializesOperationsInCallOrder()
    {
        await Sta.RunAsync(async () =>
        {
            BlockingEnteringViewModel.Reset();
            var services = CreateServices();
            services.AddTransient<BlockingEnteringViewModel>();
            services.AddView<BlockingEnteringViewModel, BlockingEnteringView>();
            using var provider = services.BuildServiceProvider();
            var regions = provider.GetRequiredService<IRegionManager>();
            var navigation = provider.GetRequiredService<INavigationService>();
            var host = new ContentControl();
            using var registration = regions.RegisterRegion("Main", host);

            Assert.True((await navigation.NavigateAsync<FirstViewModel>("Main")).Succeeded);
            var forward = navigation.NavigateAsync<BlockingEnteringViewModel>("Main");
            await BlockingEnteringViewModel.Entered.WaitAsync(TimeSpan.FromSeconds(5));

            var back = navigation.GoBackAsync("Main");
            Assert.False(back.IsCompleted);

            BlockingEnteringViewModel.AllowEntering();

            Assert.True((await forward).Succeeded);
            Assert.True((await back).Succeeded);
            Assert.IsType<FirstView>(host.Content);
        });
    }

    [Fact]
    public async Task CanGoBack_ReadsBackStackSnapshotFromBackgroundThread()
    {
        await Sta.RunAsync(async () =>
        {
            var services = CreateServices();
            using var provider = services.BuildServiceProvider();
            var regions = provider.GetRequiredService<IRegionManager>();
            var navigation = provider.GetRequiredService<INavigationService>();
            var host = new ContentControl();
            using var registration = regions.RegisterRegion("Main", host);

            await navigation.NavigateAsync<FirstViewModel>("Main");
            await navigation.NavigateAsync<SecondViewModel>("Main");

            Assert.True(await Task.Run(() => navigation.CanGoBack("Main")));
        });
    }

    [Fact]
    public async Task Navigate_WhenLifecycleCallbackAwaitsSameRegionNavigation_FailsFastInsteadOfDeadlocking()
    {
        await Sta.RunAsync(async () =>
        {
            ReentrantViewModel.Reset();
            var services = CreateServices();
            services.AddTransient<ReentrantViewModel>();
            services.AddView<ReentrantViewModel, ReentrantView>();
            using var provider = services.BuildServiceProvider();
            var regions = provider.GetRequiredService<IRegionManager>();
            var navigation = provider.GetRequiredService<INavigationService>();
            var host = new ContentControl();
            using var registration = regions.RegisterRegion("Main", host);

            ReentrantViewModel.Navigation = navigation;
            var outer = await navigation.NavigateAsync<ReentrantViewModel>("Main")
                .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(outer.Succeeded);
            Assert.IsType<ReentrantView>(host.Content);
            var inner = Assert.IsType<NavigationResult>(ReentrantViewModel.InnerResult);
            Assert.False(inner.Succeeded);
            Assert.Equal(NavigationFailureKind.Reentrant, inner.FailureKind);
            Assert.Equal("navigation.reentrant", inner.DiagnosticCode);
        });
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddWpfPresentation();
        services.AddTransient<FirstViewModel>();
        services.AddTransient<SecondViewModel>();
        services.AddView<FirstViewModel, FirstView>();
        services.AddView<SecondViewModel, SecondView>();
        services.AddView<GuardedViewModel, GuardedView>();
        return services;
    }

    private sealed class FirstViewModel : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class SecondViewModel : INavigationAware
    {
        public object? Parameter { get; private set; }

        public Task OnNavigatedToAsync(NavigationContext context, CancellationToken cancellationToken = default)
        {
            Parameter = context.Parameter;
            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync(NavigationContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class GuardedViewModel : IConfirmNavigation
    {
        public ValueTask<bool> ConfirmNavigationAsync(
            NavigationContext context,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);
    }

    private sealed class FailingEnteringViewModel : INavigationAware
    {
        public FailingEnteringViewModel(DisposalProbe probe) => _ = probe;

        public Task OnNavigatedToAsync(NavigationContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Expected entering failure.");

        public Task OnNavigatedFromAsync(NavigationContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class DisposalProbe : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class UnavailableViewModel
    {
        public UnavailableViewModel() => CreatedCount++;

        public static int CreatedCount { get; private set; }

        public static void Reset() => CreatedCount = 0;
    }

    private sealed class ThrowingDisposeViewModel : IDisposable
    {
        public void Dispose() => throw new InvalidOperationException("Expected disposal failure.");
    }

    private sealed class BlockingEnteringViewModel : INavigationAware
    {
        private static TaskCompletionSource _entered = CreateCompletionSource();
        private static TaskCompletionSource _allowEntering = CreateCompletionSource();

        public static Task Entered => _entered.Task;

        public static void Reset()
        {
            _entered = CreateCompletionSource();
            _allowEntering = CreateCompletionSource();
        }

        public static void AllowEntering() => _allowEntering.TrySetResult();

        public async Task OnNavigatedToAsync(NavigationContext context, CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await _allowEntering.Task.WaitAsync(cancellationToken);
        }

        public Task OnNavigatedFromAsync(NavigationContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        private static TaskCompletionSource CreateCompletionSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ReentrantViewModel : INavigationAware
    {
        public static INavigationService? Navigation { get; set; }

        public static NavigationResult? InnerResult { get; private set; }

        public static void Reset()
        {
            Navigation = null;
            InnerResult = null;
        }

        public async Task OnNavigatedToAsync(NavigationContext context, CancellationToken cancellationToken = default)
            => InnerResult = await Navigation!.NavigateAsync<SecondViewModel>("Main", cancellationToken: cancellationToken);

        public Task OnNavigatedFromAsync(NavigationContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FirstView : UserControl;
    private sealed class SecondView : UserControl;
    private sealed class GuardedView : UserControl;
    private sealed class FailingEnteringView : UserControl;
    private sealed class UnavailableView : UserControl;
    private sealed class ThrowingDisposeView : UserControl;
    private sealed class BlockingEnteringView : UserControl;
    private sealed class ReentrantView : UserControl;
}
