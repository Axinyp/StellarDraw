using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Axinyp.Wpf.Hosting.Abstractions.Wpf;
using Axinyp.Wpf.Hosting.Wpf.Extensions;

namespace Axinyp.Wpf.Hosting.Wpf.Tests;

public sealed class RegionManagerTests
{
    [Fact]
    public async Task Registration_DisposeUnregistersRegionAndClearsActiveScopedView()
    {
        await Sta.RunAsync(async () =>
        {
            var disposalProbe = new DisposalProbe();
            var services = new ServiceCollection();
            services.AddWpfPresentation();
            services.AddScoped(_ => disposalProbe);
            services.AddTransient<ScopedViewModel>();
            services.AddView<ScopedViewModel, ScopedView>();
            using var provider = services.BuildServiceProvider();
            var regions = provider.GetRequiredService<IRegionManager>();
            var navigation = provider.GetRequiredService<INavigationService>();
            var host = new ContentControl();

            var registration = regions.RegisterRegion("Main", host);
            Assert.True((await navigation.NavigateAsync<ScopedViewModel>("Main")).Succeeded);
            Assert.IsType<ScopedView>(host.Content);

            registration.Dispose();

            Assert.False(regions.IsRegistered("Main"));
            Assert.Null(host.Content);
            await disposalProbe.DisposedTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Throws<InvalidOperationException>(() => regions.GetRegion("Main"));
        });
    }

    [Fact]
    public async Task Registration_DisposeAfterDispatcherShutdown_ReleasesScopeWithoutVisualTreeAccess()
    {
        await using var dispatcherHost = new WpfDispatcherTestHost();
        var dispatcher = await dispatcherHost.GetDispatcherAsync();
        var disposalProbe = new DisposalProbe();
        IDisposable? registration = null;
        ServiceProvider? provider = null;

        await dispatcher.InvokeAsync(async () =>
        {
            var services = new ServiceCollection();
            services.AddWpfPresentation();
            services.AddScoped(_ => disposalProbe);
            services.AddTransient<ScopedViewModel>();
            services.AddView<ScopedViewModel, ScopedView>();
            provider = services.BuildServiceProvider();
            var regions = provider.GetRequiredService<IRegionManager>();
            var navigation = provider.GetRequiredService<INavigationService>();
            registration = regions.RegisterRegion("Main", new ContentControl());

            Assert.True((await navigation.NavigateAsync<ScopedViewModel>("Main")).Succeeded);
        }).Task.Unwrap();

        dispatcher.InvokeShutdown();
        registration!.Dispose();

        await disposalProbe.DisposedTask.WaitAsync(TimeSpan.FromSeconds(5));
        provider!.Dispose();
    }

    [Fact]
    public async Task Registration_DisposeWhenQueuedCleanupIsCancelledByShutdown_ReleasesScope()
    {
        await using var dispatcherHost = new WpfDispatcherTestHost();
        var dispatcher = await dispatcherHost.GetDispatcherAsync();
        var disposalProbe = new DisposalProbe();
        IDisposable? registration = null;
        ServiceProvider? provider = null;

        await dispatcher.InvokeAsync(async () =>
        {
            var services = new ServiceCollection();
            services.AddWpfPresentation();
            services.AddScoped(_ => disposalProbe);
            services.AddTransient<ScopedViewModel>();
            services.AddView<ScopedViewModel, ScopedView>();
            provider = services.BuildServiceProvider();
            var regions = provider.GetRequiredService<IRegionManager>();
            var navigation = provider.GetRequiredService<INavigationService>();
            registration = regions.RegisterRegion("Main", new ContentControl());

            Assert.True((await navigation.NavigateAsync<ScopedViewModel>("Main")).Succeeded);
        }).Task.Unwrap();

        using var blockerStarted = new ManualResetEventSlim();
        using var releaseBlocker = new ManualResetEventSlim();
        _ = dispatcher.BeginInvoke(
            DispatcherPriority.Normal,
            new Action(() =>
            {
                blockerStarted.Set();
                releaseBlocker.Wait();
            }));
        Assert.True(blockerStarted.Wait(TimeSpan.FromSeconds(5)));

        await Task.Run(registration!.Dispose);
        dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        releaseBlocker.Set();

        await disposalProbe.DisposedTask.WaitAsync(TimeSpan.FromSeconds(5));
        provider!.Dispose();
    }

    [Fact]
    public async Task Registration_DuplicateNameIsRejected()
    {
        await Sta.RunAsync(() =>
        {
            var services = new ServiceCollection();
            services.AddWpfPresentation();
            using var provider = services.BuildServiceProvider();
            var regions = provider.GetRequiredService<IRegionManager>();
            using var registration = regions.RegisterRegion("Main", new ContentControl());

            var exception = Assert.Throws<InvalidOperationException>(
                () => regions.RegisterRegion("Main", new ContentControl()));

            Assert.Contains("已被注册", exception.Message);
            return Task.CompletedTask;
        });
    }

    private sealed class ScopedViewModel(DisposalProbe disposalProbe) : IDisposable
    {
        public void Dispose() => disposalProbe.Dispose();
    }

    private sealed class ScopedView : UserControl;

    private sealed class DisposalProbe
    {
        private readonly TaskCompletionSource _disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DisposedTask => _disposed.Task;

        public void Dispose() => _disposed.TrySetResult();
    }
}
