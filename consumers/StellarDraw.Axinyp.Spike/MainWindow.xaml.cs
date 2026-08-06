using System.Windows;
using Axinyp.Wpf.Hosting.Abstractions.Wpf;
using Microsoft.Extensions.DependencyInjection;

namespace StellarDraw.Axinyp.Spike;

/// <summary>
/// 最小化消费者 Shell。
/// Axinyp Region 与 legacy plugin View 分别由不同的内容槽位拥有，避免混淆生命周期。
/// </summary>
public partial class MainWindow : Window
{
    private const string StatusRegionName = "Spike.Status";
    private readonly IRegionManager _regionManager;
    private readonly INavigationService _navigationService;
    private readonly MainWindowViewModel _viewModel;
    private readonly LegacyWhiteboardPluginBridge _legacyPluginBridge;
    private readonly LegacyPluginOptions _legacyPluginOptions;
    private IDisposable? _statusRegionRegistration;
    private bool _initialized;

    /// <summary>
    /// 由 Axinyp 窗口管理器创建 Shell。
    /// 公开签名仅暴露框架通用的服务提供程序，legacy bridge 及其选项保持消费者内部实现。
    /// </summary>
    public MainWindow(IServiceProvider services)
        : this(
            services.GetRequiredService<IRegionManager>(),
            services.GetRequiredService<INavigationService>(),
            services.GetRequiredService<MainWindowViewModel>(),
            services.GetRequiredService<LegacyWhiteboardPluginBridge>(),
            services.GetRequiredService<LegacyPluginOptions>())
    {
    }

    internal MainWindow(
        IRegionManager regionManager,
        INavigationService navigationService,
        MainWindowViewModel viewModel,
        LegacyWhiteboardPluginBridge legacyPluginBridge,
        LegacyPluginOptions legacyPluginOptions)
    {
        _regionManager = regionManager;
        _navigationService = navigationService;
        _viewModel = viewModel;
        _legacyPluginBridge = legacyPluginBridge;
        _legacyPluginOptions = legacyPluginOptions;

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
            _statusRegionRegistration = _regionManager.RegisterRegion(StatusRegionName, StatusRegionHost);
            var result = await _navigationService.NavigateAsync<StatusRegionViewModel>(StatusRegionName);
            if (!result.Succeeded)
            {
                _viewModel.Status = $"Axinyp Region 初始化失败：{result.ErrorMessage}";
                return;
            }

            if (_legacyPluginOptions.AssemblyPath is null)
            {
                _viewModel.Status = "Axinyp Shell、主题、本地化和 Region 导航均已启动；legacy 白板插件尚未加载。";
                return;
            }

            await _legacyPluginBridge.LoadAsync(_legacyPluginOptions.AssemblyPath, PluginHost);
            _viewModel.Status = "Axinyp Shell、主题、本地化、Region 导航和 legacy 白板插件均已启动。";
        }
        catch (Exception ex)
        {
            _viewModel.Status = $"Spike 初始化失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 供 App 层协调停机（WpfApplicationLifecycle.BeforeHostStopAsync）在停止宿主前卸载 legacy 插件。
    /// 未加载时为无操作；卸载异常向上传播，由协调停机统一按「保持窗口打开 → 二次关闭强制退出」处理。
    /// </summary>
    internal async Task UnloadLegacyPluginAsync()
    {
        if (!_legacyPluginBridge.IsLoaded)
            return;

        await _legacyPluginBridge.UnloadAsync(PluginHost);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _statusRegionRegistration?.Dispose();
        _statusRegionRegistration = null;
    }
}
