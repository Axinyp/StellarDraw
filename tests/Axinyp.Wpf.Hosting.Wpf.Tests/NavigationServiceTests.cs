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
            var services = CreateServices();
            services.AddSingleton<GuardedViewModel>();
            using var provider = services.BuildServiceProvider();
            var regions = provider.GetRequiredService<IRegionManager>();
            var navigation = provider.GetRequiredService<INavigationService>();
            var host = new ContentControl();
            using var registration = regions.RegisterRegion("Main", host);

            Assert.True((await navigation.NavigateAsync<GuardedViewModel>("Main")).Succeeded);
            var guarded = Assert.IsType<GuardedView>(host.Content);

            var result = await navigation.NavigateAsync<SecondViewModel>("Main");

            Assert.True(result.Cancelled);
            Assert.Same(guarded, host.Content);
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
        public ValueTask<bool> ConfirmNavigationAsync(NavigationContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);
    }

    private sealed class FirstView : UserControl;
    private sealed class SecondView : UserControl;
    private sealed class GuardedView : UserControl;
}

internal static class Sta
{
    private static readonly Lazy<StaDispatcher> SharedDispatcher = new(() => new StaDispatcher());

    public static Task RunAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return SharedDispatcher.Value.RunAsync(action);
    }

    private sealed class StaDispatcher
    {
        private readonly TaskCompletionSource<System.Windows.Threading.Dispatcher> _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StaDispatcher()
        {
            var thread = new Thread(() =>
            {
                var application = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
                _ready.TrySetResult(application.Dispatcher);
                System.Windows.Threading.Dispatcher.Run();
            })
            {
                IsBackground = true
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        public async Task RunAsync(Func<Task> action)
        {
            var dispatcher = await _ready.Task.ConfigureAwait(false);
            await dispatcher.InvokeAsync(action).Task.Unwrap().ConfigureAwait(false);
        }
    }
}
