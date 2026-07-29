using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Axinyp.Wpf.Hosting.Abstractions.Wpf;
using Axinyp.Wpf.Hosting.Wpf.Extensions;

namespace Axinyp.Wpf.Hosting.Wpf.Tests;

public sealed class WindowManagerTests
{
    [Fact]
    public async Task ShowAsync_CreatesMappedWindowBindsViewModelAndDisposesScopeWhenClosed()
    {
        await Sta.RunAsync(async () =>
        {
            var probe = new DisposalProbe();
            var services = new ServiceCollection();
            services.AddWpfPresentation();
            services.AddScoped(_ => probe);
            services.AddScoped<ScopedWindowViewModel>();
            services.AddView<ScopedWindowViewModel, ScopedWindow>();
            using var provider = services.BuildServiceProvider();
            var windows = provider.GetRequiredService<IWindowManager>();

            var window = await windows.ShowAsync<ScopedWindowViewModel>();

            Assert.IsType<ScopedWindow>(window);
            Assert.IsType<ScopedWindowViewModel>(window.DataContext);
            Assert.True(window.IsVisible);
            Assert.False(probe.Disposed);

            window.Close();

            Assert.True(probe.Disposed);
        });
    }

    [Fact]
    public async Task ShowAsync_RejectsMappedViewThatIsNotWindowAndDisposesScope()
    {
        await Sta.RunAsync(async () =>
        {
            var probe = new DisposalProbe();
            var services = new ServiceCollection();
            services.AddWpfPresentation();
            services.AddScoped(_ => probe);
            services.AddScoped<NonWindowViewModel>();
            services.AddView<NonWindowViewModel, NonWindowView>();
            using var provider = services.BuildServiceProvider();
            var windows = provider.GetRequiredService<IWindowManager>();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => windows.ShowAsync<NonWindowViewModel>());

            Assert.Contains("不是 Window", exception.Message);
            Assert.True(probe.Disposed);
        });
    }

    [Fact]
    public async Task ShowAsync_RejectsOwnerThatIsNotVisible()
    {
        await Sta.RunAsync(async () =>
        {
            var services = new ServiceCollection();
            services.AddWpfPresentation();
            services.AddTransient<WindowViewModel>();
            services.AddView<WindowViewModel, ScopedWindow>();
            using var provider = services.BuildServiceProvider();
            var windows = provider.GetRequiredService<IWindowManager>();
            var owner = new Window();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => windows.ShowAsync<WindowViewModel>(owner));

            Assert.Contains("仍处于显示状态", exception.Message);
        });
    }

    private sealed class ScopedWindowViewModel(DisposalProbe probe) : IDisposable
    {
        public void Dispose() => probe.Disposed = true;
    }

    private sealed class NonWindowViewModel(DisposalProbe probe) : IDisposable
    {
        public void Dispose() => probe.Disposed = true;
    }

    private sealed class WindowViewModel;

    private sealed class ScopedWindow : Window;
    private sealed class NonWindowView : UserControl;

    private sealed class DisposalProbe
    {
        public bool Disposed { get; set; }
    }
}
