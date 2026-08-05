using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Axinyp.Wpf.Hosting.Extensions;
using Axinyp.Wpf.Hosting.Abstractions.Wpf;
using Axinyp.Wpf.Hosting.Abstractions.Wpf.Themes;
using Axinyp.Wpf.Hosting.Wpf.Extensions;
using WhiteBoard.Client.Internal.PluginHosting;
using WhiteBoard.Client.ViewModels;
using WhiteBoard.Client.Views;

namespace WhiteBoard.Client;

/// <summary>
/// WhiteBoard.Client 的 WPF 宿主入口。
/// 当前实现遵循“先稳定宿主，再最小接入真实业务”的策略：
/// 1. 由 Axinyp Generic Host 统一负责启动、停止和失败回滚；
/// 2. WhiteBoard.Client 自身只承担正式 Shell 宿主责任；
/// 3. 真实白板业务继续由 <c>WhiteBoard.Plugin</c> 持有，宿主只负责加载插件、承载视图与协调退出顺序。
/// 这样可以最大限度复用 Demo 已验证过的白板插件链路，同时避免在首轮回接中重写领域对象和 UI 组合逻辑。
/// </summary>
public partial class App : Application
{
    private readonly PluginManager _pluginManager = new();
    private readonly InMemoryMessageBus _messageBus = new();
    private readonly ClientHostCapabilities _hostCapabilities = ClientHostCapabilities.Instance;
    private IHost? _host;
    private bool _shutdownStarted;
    private bool _shutdownCompleted;

    /// <summary>
    /// 启动客户端应用并构建 Axinyp 模块化 WPF 宿主。
    /// 这里采用与 <c>samples/BasicHost</c> / <c>consumer-smoke</c> 一致的标准启动顺序：
    /// 先注册 Host、设置、本地化、WPF Presentation、主题和 View 映射，
    /// 再统一 Build/Start，最后通过 <see cref="IWindowManager"/> 显示主窗口。
    /// 在主窗口成功显示后，才开始加载真实白板插件，确保：
    /// - 主题与本地化资源已就绪；
    /// - 窗口容器已经可承载插件视图；
    /// - 若插件失败，仍能把问题隔离在“业务接入层”，而不是污染宿主初始化链路。
    /// </summary>
    /// <param name="e">启动参数。</param>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var builder = Host.CreateApplicationBuilder(e.Args);
            builder.AddModularHosting();
            builder.Services.AddJsonSettings(options => options.ApplicationName = "StellarDraw");
            builder.Services.AddResxLocalization(options =>
            {
                options.DefaultCulture = CultureInfo.GetCultureInfo("zh-CN");
                options.SupportedCultures.Add(CultureInfo.GetCultureInfo("zh-CN"));
                options.SupportedCultures.Add(CultureInfo.GetCultureInfo("en-US"));
                options.ResourceManagers.Add(new ResourceManager("WhiteBoard.Client.Resources.Strings", typeof(App).Assembly));
            });
            builder.Services.AddWpfPresentation();
            builder.Services.AddWpfLocalizationBindings();
            builder.Services.AddWpfThemes(options =>
            {
                options.Themes.Add(new ThemeDescriptor("light", "Light",
                    new Uri("pack://application:,,,/WhiteBoard.Client;component/Themes/Light.xaml"), true));
                options.Themes.Add(new ThemeDescriptor("dark", "Dark",
                    new Uri("pack://application:,,,/WhiteBoard.Client;component/Themes/Dark.xaml")));
            });

            builder.Services.AddSingleton<MainWindowViewModel>();
            builder.Services.AddTransient<ClientHomeViewModel>();
            builder.Services.AddView<MainWindowViewModel, MainWindow>();
            builder.Services.AddView<ClientHomeViewModel, ClientHomeView>();

            _host = builder.BuildModularHost();
            await _host.StartModularHostAsync();

            var windowManager = _host.Services.GetRequiredService<IWindowManager>();
            var shell = (MainWindow)await windowManager.ShowAsync<MainWindowViewModel>();
            MainWindow = shell;
            shell.Closing += OnMainWindowClosing;
            shell.ShowStatus("宿主已启动，正在加载白板插件…");

            await LoadPluginAsync(shell);
            ScheduleAutoCloseForVerification(shell);
        }
        catch (Exception ex)
        {
            var logger = _host?.Services.GetService<ILogger<App>>();
            logger?.LogCritical(ex, "WhiteBoard.Client 启动失败。");
            if (_host is not null)
            {
                try
                {
                    await _host.StopModularHostAsync();
                }
                catch (Exception stopException)
                {
                    logger?.LogError(stopException, "WhiteBoard.Client 启动失败后的宿主停止失败。");
                }
                finally
                {
                    _host.Dispose();
                    _host = null;
                }
            }

            MessageBox.Show("客户端无法启动，请查看应用日志后重试。", "WhiteBoard.Client 启动失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    /// <summary>
    /// 拦截主窗口关闭并在真正退出进程前完成完整的异步收尾。
    /// <para>
    /// 不能把插件卸载和宿主停止完全留给 <c>OnExit async void</c>，
    /// 因为 WPF 不会等待其中的异步续体全部完成，进程可能在 <c>await</c> 后半段直接结束，
    /// 从而导致只看到“开始 StopAsync”而看不到“视图脱离 / ALC 卸载完成”的日志。
    /// </para>
    /// 因此这里采用“第一次 Closing 先取消 → 异步完成释放 → 第二次真正关闭”的模式，
    /// 保证插件和宿主资源在窗口销毁前就已完成清理。
    /// </summary>
    private async void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_shutdownCompleted)
            return;

        if (_shutdownStarted)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        _shutdownStarted = true;

        var shell = sender as MainWindow;
        shell?.ShowStatus("正在关闭白板插件与宿主…");
        Console.WriteLine("[Client:Shutdown] Begin coordinated shutdown.");

        try
        {
            await TearDownPluginAsync();
            Console.WriteLine("[Client:Shutdown] Plugin teardown completed.");

            if (_host is not null)
            {
                await _host.StopModularHostAsync();
                Console.WriteLine("[Client:Shutdown] Host stop completed.");
                _host.Dispose();
                _host = null;
                Console.WriteLine("[Client:Shutdown] Host dispose completed.");
            }

            _shutdownCompleted = true;
            shell?.Close();
        }
        catch (Exception ex)
        {
            _shutdownStarted = false;
            _host?.Services.GetService<ILogger<App>>()?.LogError(ex, "WhiteBoard.Client 关闭失败。");
            shell?.ShowStatus("关闭未能完成，请查看应用日志后重试。");
            MessageBox.Show("客户端关闭未能完成，请查看应用日志后重试。", "WhiteBoard.Client 关闭失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// OnExit 现在只保留兜底清理责任。
    /// 正常情况下，完整的插件卸载和宿主停止都应在 <see cref="OnMainWindowClosing"/> 中完成；
    /// 这里不再做异步释放主逻辑，避免再次出现“退出事件本身不会等待异步收尾”的问题。
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _host = null;

        // 只有在主窗口真正关闭、宿主也完成 Dispose 之后，
        // 再做 ALC 回收验证才更接近真实结果。
        // 否则即便插件视图已从 Shell 脱离，WPF/Host 仍可能在关闭尾声阶段暂时保留引用，
        // 导致我们把“还没来得及释放”误判成“永久泄漏”。
        _pluginManager.VerifyAlcUnloaded();
        base.OnExit(e);
    }

    /// <summary>
    /// 加载白板插件并将其视图挂到主窗口容器上。
    /// 首轮回接阶段如果插件未找到或加载失败，不直接杀掉宿主，
    /// 而是把状态显示在 Shell 上，方便优先诊断“插件链路问题”而不是把症状混淆为 Host 启动失败。
    /// </summary>
    private async Task LoadPluginAsync(MainWindow shell)
    {
        var pluginDllPath = FindPluginDll();
        if (pluginDllPath is null)
        {
            shell.ShowStatus($"未找到插件 DLL，请先构建 WhiteBoard.Plugin：{DefaultPluginDir()}");
            return;
        }

        try
        {
            await _pluginManager.LoadAsync(pluginDllPath, _messageBus, _hostCapabilities);
            if (_pluginManager.ViewHandle is not null)
                shell.AttachPluginView(_pluginManager.ViewHandle);
        }
        catch (Exception ex)
        {
            _host?.Services.GetService<ILogger<App>>()?.LogError(ex, "白板插件加载失败。");
            shell.ShowStatus("白板插件未能加载，请查看应用日志。");
            MessageBox.Show("白板插件未能加载，请查看应用日志。", "插件加载失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 为自动化验证提供一个可选的自动关闭钩子。
    /// 当设置环境变量 <c>WHITEBOARD_CLIENT_AUTOCLOSE_SECONDS</c> 时，
    /// 客户端会在成功启动并尝试加载插件后等待指定秒数，再通过 UI 线程关闭主窗口。
    /// 这样可以在无人值守的运行验证里覆盖正常退出路径，观察插件卸载和宿主停止日志，
    /// 同时又不会影响平时人工调试和正常使用。
    /// </summary>
    private static void ScheduleAutoCloseForVerification(MainWindow shell)
    {
        var raw = Environment.GetEnvironmentVariable("WHITEBOARD_CLIENT_AUTOCLOSE_SECONDS");
        if (!int.TryParse(raw, out var seconds) || seconds <= 0)
            return;

        shell.ShowStatus($"自动验证模式已启用，将在 {seconds} 秒后关闭窗口…");
        Console.WriteLine($"[Client:AutoClose] Scheduled after {seconds} seconds.");

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            await shell.Dispatcher.InvokeAsync(shell.Close);
        });
    }

    /// <summary>
    /// 先解除主窗口对插件视图的引用，再卸载插件。
    /// 这是首轮回接里必须显式保留的防泄漏步骤，
    /// 因为即便插件自身正确调用了 <c>DetachAsync</c>，如果 Shell 仍然把根视图挂在视觉树上，
    /// WPF 仍可能通过 DataContext、样式、事件路由或资源引用链持有插件对象。
    /// 在调用卸载前，额外等待一轮 Dispatcher 空闲帧，尽量让视觉树移除操作和绑定清理先落地。
    /// </summary>
    private async Task TearDownPluginAsync()
    {
        if (MainWindow is MainWindow shell)
        {
            shell.ClearPluginView();
            shell.UpdateLayout();
            await shell.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }

        await _pluginManager.UnloadAsync();

        if (MainWindow is MainWindow currentShell)
            await currentShell.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static string? FindPluginDll()
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "plugins", "whiteboard", "WhiteBoard.Plugin.dll"),
            Path.Combine(baseDirectory, "WhiteBoard.Plugin.dll"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string DefaultPluginDir()
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "whiteboard");
}
