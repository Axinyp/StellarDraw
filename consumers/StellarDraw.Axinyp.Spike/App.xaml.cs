using System.Globalization;
using System.Resources;
using System.Windows;
using Axinyp.Wpf.Hosting.Abstractions.Wpf;
using Axinyp.Wpf.Hosting.Abstractions.Wpf.Themes;
using Axinyp.Wpf.Hosting.Extensions;
using Axinyp.Wpf.Hosting.Wpf.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace StellarDraw.Axinyp.Spike;

/// <summary>
/// Axinyp 白板消费者 Spike 的应用入口。
/// 此处只组合基础设施；legacy 白板插件由消费者私有 bridge 按需加载。
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    /// <summary>
    /// 构建并启动模块化 WPF 宿主。
    /// </summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var builder = Host.CreateApplicationBuilder(e.Args);
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
            builder.Services.AddSingleton(LegacyPluginOptions.FromArguments(e.Args));
            builder.Services.AddSingleton<LegacyWhiteboardPluginBridge>();
            builder.Services.AddView<MainWindowViewModel, MainWindow>();
            builder.Services.AddView<StatusRegionViewModel, StatusRegionView>();

            _host = builder.BuildModularHost();
            await _host.StartModularHostAsync();

            var windowManager = _host.Services.GetRequiredService<IWindowManager>();
            MainWindow = await windowManager.ShowAsync<MainWindowViewModel>();
        }
        catch
        {
            _host?.Dispose();
            _host = null;
            Shutdown(-1);
            throw;
        }
    }

    /// <summary>
    /// 退出时在释放 DI 容器前停止模块化宿主。
    /// </summary>
    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopModularHostAsync();
            _host.Dispose();
            _host = null;
        }

        base.OnExit(e);
    }
}
