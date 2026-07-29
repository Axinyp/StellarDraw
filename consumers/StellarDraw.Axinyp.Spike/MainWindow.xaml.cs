using System.ComponentModel;
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
    private bool _isClosing;

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
        Closing += OnClosing;
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

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_isClosing || !_legacyPluginBridge.IsLoaded)
            return;

        e.Cancel = true;
        _isClosing = true;
        _ = UnloadAndCloseAsync();
    }

    private async Task UnloadAndCloseAsync()
    {
        try
        {
            await _legacyPluginBridge.UnloadAsync(PluginHost);
        }
        catch (Exception ex)
        {
            _viewModel.Status = $"Legacy 白板插件卸载出现异常：{ex.Message}";
        }
        finally
        {
            _ = Dispatcher.InvokeAsync(Close);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _statusRegionRegistration?.Dispose();
        _statusRegionRegistration = null;
    }
}
