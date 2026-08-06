using System.Globalization;
using System.Resources;
using System.Windows;
using Axinyp.Wpf.Hosting.Abstractions.Wpf;
using Axinyp.Wpf.Hosting.Abstractions.Wpf.Themes;
using Axinyp.Wpf.Hosting.Extensions;
using Axinyp.Wpf.Hosting.Wpf;
using Axinyp.Wpf.Hosting.Wpf.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace StellarDraw.Axinyp.Spike;

/// <summary>
/// Axinyp 白板消费者 Spike 的应用入口。
/// 此处只组合基础设施；legacy 白板插件由消费者私有 bridge 按需加载。
/// 启停编排交给框架的 <see cref="WpfApplicationLifecycle"/>：
/// 启动失败会记录并提示后以 -1 退出，窗口关闭走「先卸插件、再停宿主、后真正关闭」的协调停机。
/// </summary>
public partial class App : Application
{
    /// <summary>构建并启动模块化 WPF 宿主。</summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        MainWindow? shell = null;
        await WpfApplicationLifecycle.RunAsync(this, new WpfApplicationLifecycleOptions
        {
            HostFactory = () => BuildHost(e.Args),
            ShellFactory = async host =>
            {
                var windowManager = host.Services.GetRequiredService<IWindowManager>();
                shell = (MainWindow)await windowManager.ShowAsync<MainWindowViewModel>();
                return shell;
            },
            BeforeHostStopAsync = () => shell?.UnloadLegacyPluginAsync() ?? Task.CompletedTask,
            FailureCaption = "StellarDraw Spike",
            StartupFailureMessage = "Spike 无法启动，请查看应用日志后重试。",
            ShutdownFailureMessage = "Spike 关闭未能完成，请查看应用日志；再次关闭窗口将强制退出。",
        });
    }

    private static IHost BuildHost(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.AddModularHosting();
        builder.Services.AddJsonSettings(options => options.ApplicationName = "StellarDraw.Axinyp.Spike");
        builder.Services.AddResxLocalization(options =>
        {
            options.DefaultCulture = CultureInfo.GetCultureInfo("zh-CN");
            options.SupportedCultures.Add(CultureInfo.GetCultureInfo("zh-CN"));
            options.SupportedCultures.Add(CultureInfo.GetCultureInfo("en-US"));
            options.ResourceManagers.Add(
                new ResourceManager("StellarDraw.Axinyp.Spike.Resources.Strings", typeof(App).Assembly));
        });
        builder.Services.AddWpfPresentation();
        builder.Services.AddWpfLocalizationBindings();
        builder.Services.AddWpfThemes(options =>
        {
            options.Themes.Add(new ThemeDescriptor(
                "light",
                "Light",
                new Uri("pack://application:,,,/StellarDraw.Axinyp.Spike;component/Themes/Light.xaml"),
                true));
            options.Themes.Add(new ThemeDescriptor(
                "dark",
                "Dark",
                new Uri("pack://application:,,,/StellarDraw.Axinyp.Spike;component/Themes/Dark.xaml")));
        });

        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddTransient<StatusRegionViewModel>();
        builder.Services.AddSingleton(LegacyPluginOptions.FromArguments(args));
        builder.Services.AddSingleton<LegacyWhiteboardPluginBridge>();
        builder.Services.AddView<MainWindowViewModel, MainWindow>();
        builder.Services.AddView<StatusRegionViewModel, StatusRegionView>();

        return builder.BuildModularHost();
    }
}
