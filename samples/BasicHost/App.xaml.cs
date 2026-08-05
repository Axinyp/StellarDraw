using System.Globalization;
using System.Resources;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Axinyp.Wpf.Hosting.Extensions;
using Axinyp.Wpf.Hosting.Samples.BasicHost.ViewModels;
using Axinyp.Wpf.Hosting.Abstractions.Wpf;
using Axinyp.Wpf.Hosting.Wpf.Extensions;
using Axinyp.Wpf.Hosting.Abstractions.Wpf.Themes;

namespace Axinyp.Wpf.Hosting.Samples.BasicHost;

/// <summary>
/// 基础样例宿主入口。
/// 演示如何基于「Generic Host + DI + 显式 View 注册」启动一个最小 WPF 应用。
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    /// <summary>
    /// 启动样例应用并构建模块化 WPF 宿主。
    /// </summary>
    /// <param name="e">启动参数。</param>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var builder = Host.CreateApplicationBuilder(e.Args);
            builder.AddModularHosting();
            builder.Services.AddJsonSettings(options => options.ApplicationName = "Axinyp.Wpf.Hosting.BasicHost");
            builder.Services.AddResxLocalization(options =>
            {
                options.DefaultCulture = CultureInfo.GetCultureInfo("zh-CN");
                options.SupportedCultures.Add(CultureInfo.GetCultureInfo("zh-CN"));
                options.SupportedCultures.Add(CultureInfo.GetCultureInfo("en-US"));
                options.ResourceManagers.Add(new ResourceManager("Axinyp.Wpf.Hosting.Samples.BasicHost.Resources.Strings", typeof(App).Assembly));
            });
            builder.Services.AddWpfPresentation();
            builder.Services.AddWpfLocalizationBindings();
            builder.Services.AddWpfThemes(options =>
            {
                options.Themes.Add(new ThemeDescriptor("light", "Light",
                    new Uri("pack://application:,,,/Axinyp.Wpf.Hosting.Samples.BasicHost;component/Themes/Light.xaml"), true));
                options.Themes.Add(new ThemeDescriptor("dark", "Dark",
                    new Uri("pack://application:,,,/Axinyp.Wpf.Hosting.Samples.BasicHost;component/Themes/Dark.xaml")));
            });

            // ViewModel 生命周期由业务决定；View 映射显式注册且 View 始终保持 transient。
            builder.Services.AddSingleton<MainWindowViewModel>();
            builder.Services.AddView<MainWindowViewModel, MainWindow>();

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
    /// 退出样例应用并停止模块化宿主。
    /// </summary>
    /// <param name="e">退出事件参数。</param>
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
