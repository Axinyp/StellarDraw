using System.Windows;
using System.Windows.Controls;
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
            Assert.True(disposalProbe.Disposed);
            Assert.Throws<InvalidOperationException>(() => regions.GetRegion("Main"));
        });
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
        public void Dispose() => disposalProbe.Disposed = true;
    }

    private sealed class ScopedView : UserControl;

    private sealed class DisposalProbe
    {
        public bool Disposed { get; set; }
    }
}
