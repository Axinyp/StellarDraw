using System.ComponentModel;

namespace WhiteBoard.Client.Tests;

public sealed class MainWindowShellTests
{
    [Fact]
    public async Task Shell_Loaded_NavigatesToClientHomeAndUnregistersRegionWhenClosed()
    {
        await Sta.RunAsync(async () =>
        {
            var services = CreateServices(includeHomeView: true);
            using var provider = services.BuildServiceProvider();
            var regions = provider.GetRequiredService<IRegionManager>();
            var viewModel = provider.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow(provider);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();

            window.Show();
            await WaitForInitializationAsync(viewModel);

            var regionHost = Assert.IsType<ContentControl>(window.FindName("MainRegionHost"));
            Assert.IsType<ClientHomeView>(regionHost.Content);
            Assert.False(viewModel.HasShellError);
            Assert.False(viewModel.IsInitializing);
            Assert.True(regions.IsRegistered("WhiteBoard.Client.Main"));
            Assert.Null(Assert.IsType<ContentControl>(window.FindName("PluginHost")).Content);

            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(regions.IsRegistered("WhiteBoard.Client.Main"));
            Assert.Null(regionHost.Content);
        });
    }

    [Fact]
    public async Task Shell_Loaded_WhenNavigationCannotCreateView_ShowsSafeDiagnosticAndKeepsPluginHostEmpty()
    {
        await Sta.RunAsync(async () =>
        {
            var services = CreateServices(includeHomeView: false);
            using var provider = services.BuildServiceProvider();
            var regions = provider.GetRequiredService<IRegionManager>();
            var viewModel = provider.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow(provider);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();

            window.Show();
            await WaitForInitializationAsync(viewModel);

            Assert.True(viewModel.HasShellError);
            Assert.Equal("navigation.view-composition-failed", viewModel.ShellDiagnosticCode);
            Assert.Equal("Shell 初始化未能完成，请查看应用日志。", viewModel.ShellMessage);
            Assert.Null(Assert.IsType<ContentControl>(window.FindName("MainRegionHost")).Content);
            Assert.Null(Assert.IsType<ContentControl>(window.FindName("PluginHost")).Content);

            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(regions.IsRegistered("WhiteBoard.Client.Main"));
        });
    }

    [Fact]
    public void ShellStatus_AfterFailure_DoesNotReplaceSafeFailureMessage()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.ReportShellFailure("Shell 初始化未能完成，请查看应用日志。", "navigation.view-composition-failed");

        viewModel.ReportShellStatus("白板插件未能加载，请查看应用日志。");

        Assert.Equal("Shell 初始化未能完成，请查看应用日志。", viewModel.ShellMessage);
        Assert.Equal("navigation.view-composition-failed", viewModel.ShellDiagnosticCode);
    }

    private static ServiceCollection CreateServices(bool includeHomeView)
    {
        var services = new ServiceCollection();
        services.AddWpfPresentation();
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<ClientHomeViewModel>();
        if (includeHomeView)
            services.AddView<ClientHomeViewModel, ClientHomeView>();
        return services;
    }

    private static async Task WaitForInitializationAsync(MainWindowViewModel viewModel)
    {
        if (!viewModel.IsInitializing)
            return;

        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.IsInitializing) && !viewModel.IsInitializing)
                completed.TrySetResult();
        };
        viewModel.PropertyChanged += handler;
        try
        {
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            viewModel.PropertyChanged -= handler;
        }
    }
}
