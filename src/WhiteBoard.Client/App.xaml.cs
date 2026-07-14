using System.Windows;
using System.Globalization;
using System.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WhiteBoard.Client.ViewModels;
using WhiteBoard.Framework.Hosting.Extensions;
using WhiteBoard.Framework.Hosting.Wpf;
using WhiteBoard.Framework.Hosting.Wpf.Extensions;
using WhiteBoard.Framework.Hosting.Wpf.Themes;

namespace WhiteBoard.Client;

/// <summary>
/// WPF 客户端宿主入口。
/// 基于「通用主机(Generic Host) + DI」组装；视图与 ViewModel 通过显式注册交给
/// IWindowManager 在 UI 线程和独立窗口 scope 内创建、绑定与显示。
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var builder = Host.CreateApplicationBuilder(e.Args);
            builder.AddWhiteBoardFramework();
            builder.Services.AddWhiteBoardSettings(options => options.ApplicationName = "StellarDraw");
            builder.Services.AddWhiteBoardLocalization(options =>
            {
                options.DefaultCulture = CultureInfo.GetCultureInfo("zh-CN");
                options.SupportedCultures.Add(CultureInfo.GetCultureInfo("zh-CN"));
                options.SupportedCultures.Add(CultureInfo.GetCultureInfo("en-US"));
                options.ResourceManagers.Add(new ResourceManager("WhiteBoard.Client.Resources.Strings", typeof(App).Assembly));
            });
            builder.Services.AddWhiteBoardFrameworkWpf();
            builder.Services.AddWhiteBoardWpfLocalization();
            builder.Services.AddWhiteBoardWpfThemes(options =>
            {
                options.Themes.Add(new ThemeDescriptor("light", "Light",
                    new Uri("pack://application:,,,/WhiteBoard.Client;component/Themes/Light.xaml"), true));
                options.Themes.Add(new ThemeDescriptor("dark", "Dark",
                    new Uri("pack://application:,,,/WhiteBoard.Client;component/Themes/Dark.xaml")));
            });

            // ViewModel 生命周期由业务决定；View 映射明确且 View 始终 transient。
            builder.Services.AddSingleton<MainWindowViewModel>();
            builder.Services.AddView<MainWindowViewModel, MainWindow>();

            _host = builder.BuildWhiteBoardFrameworkHost();
            await _host.StartWhiteBoardFrameworkAsync();

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

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopWhiteBoardFrameworkAsync();
            _host.Dispose();
            _host = null;
        }
        base.OnExit(e);
    }
}
