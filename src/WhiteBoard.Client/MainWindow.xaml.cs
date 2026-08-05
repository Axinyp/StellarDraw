using System.ComponentModel;
using System.Windows;
using Axinyp.Wpf.Hosting.Abstractions.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WhiteBoard.Client.ViewModels;
using WhiteBoard.Client.Views;
using WhiteBoard.Contracts.Wpf;

namespace WhiteBoard.Client;

/// <summary>
/// WhiteBoard.Client 的主窗口宿主。
/// Axinyp Region 与 legacy 插件视图使用不同内容槽位和生命周期：窗口只管理前者的注册与导航，
/// 仍由 App 与私有 <c>PluginManager</c> 管理后者。
/// </summary>
public partial class MainWindow : Window
{
    private const string MainRegionName = "WhiteBoard.Client.Main";
    private readonly IRegionManager _regionManager;
    private readonly INavigationService _navigationService;
    private readonly MainWindowViewModel _viewModel;
    private readonly ILogger<MainWindow> _logger;
    private IDisposable? _mainRegionRegistration;
    private bool _initialized;

    /// <summary>
    /// 由 Axinyp 窗口管理器通过当前窗口作用域创建 Shell。
    /// </summary>
    /// <param name="services">当前窗口作用域的服务提供程序。</param>
    public MainWindow(IServiceProvider services)
        : this(
            services.GetRequiredService<IRegionManager>(),
            services.GetRequiredService<INavigationService>(),
            services.GetRequiredService<MainWindowViewModel>(),
            services.GetRequiredService<ILogger<MainWindow>>())
    {
    }

    internal MainWindow(
        IRegionManager regionManager,
        INavigationService navigationService,
        MainWindowViewModel viewModel,
        ILogger<MainWindow> logger)
    {
        _regionManager = regionManager;
        _navigationService = navigationService;
        _viewModel = viewModel;
        _logger = logger;

        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
            return;

        _initialized = true;
        try
        {
            _mainRegionRegistration = _regionManager.RegisterRegion(MainRegionName, MainRegionHost);
            var result = await _navigationService.NavigateAsync<ClientHomeViewModel>(MainRegionName);
            if (result.Succeeded)
            {
                _viewModel.ReportShellReady("Axinyp Shell 与 Region 导航已启动。");
                return;
            }

            _viewModel.ReportShellFailure(
                "Shell 初始化未能完成，请查看应用日志。",
                result.DiagnosticCode);
            _logger.LogError(
                "WhiteBoard.Client Shell 导航失败。{FailureKind} {DiagnosticCode}",
                result.FailureKind,
                result.DiagnosticCode);
        }
        catch (Exception ex)
        {
            _viewModel.ReportShellFailure("Shell 初始化未能完成，请查看应用日志。");
            _logger.LogError(ex, "WhiteBoard.Client Shell 初始化失败。");
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _mainRegionRegistration?.Dispose();
        _mainRegionRegistration = null;
    }

    /// <summary>
    /// 将插件返回的根视图嵌入 legacy 插件容器。
    /// </summary>
    /// <param name="viewHandle">插件视图句柄。</param>
    public void AttachPluginView(IPluginViewHandle viewHandle)
    {
        ArgumentNullException.ThrowIfNull(viewHandle);
        PluginHost.Content = viewHandle.View;
        ShowStatus($"白板插件已加载：{viewHandle.PluginId}");
    }

    /// <summary>
    /// 在 legacy 插件卸载前解除主窗口对其视图的引用。
    /// </summary>
    public void ClearPluginView() => PluginHost.Content = null;

    /// <summary>
    /// 更新已脱敏的 Shell 状态。
    /// </summary>
    /// <param name="text">用户可见状态。</param>
    public void ShowStatus(string text) => _viewModel.ReportShellStatus(text);
}
