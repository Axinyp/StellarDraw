using System.Globalization;
using System.Resources;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Axinyp.Wpf.Hosting.Extensions;
using Axinyp.Wpf.Hosting.Abstractions.Wpf;
using Axinyp.Wpf.Hosting.Abstractions.Wpf.Themes;
using Axinyp.Wpf.Hosting.Wpf.Extensions;

namespace ConsumerSmoke;

/// <summary>
/// 本地 NuGet 消费验证入口。
/// 该项目不依赖源码 ProjectReference，只依赖刚刚打出的本地包，
/// 用于验证包消费者是否能按 README 的方式完成恢复、编译与最小宿主装配。
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    /// <summary>
    /// 启动临时消费验证应用。
    /// 这里的代码刻意保持最小化，只验证包接入路径，不做实际运行展示。
    /// </summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var builder = Host.CreateApplicationBuilder(e.Args);
            builder.AddModularHosting();
            builder.Services.AddJsonSettings(options => options.ApplicationName = "ConsumerSmoke");
            builder.Services.AddResxLocalization(options =>
            {
                options.DefaultCulture = CultureInfo.GetCultureInfo("zh-CN");
                options.SupportedCultures.Add(CultureInfo.GetCultureInfo("zh-CN"));
                options.SupportedCultures.Add(CultureInfo.GetCultureInfo("en-US"));
                options.ResourceManagers.Add(new ResourceManager("ConsumerSmoke.Resources.Strings", typeof(App).Assembly));
            });
            builder.Services.AddWpfPresentation();
            builder.Services.AddWpfLocalizationBindings();
            builder.Services.AddWpfThemes(options =>
            {
                options.Themes.Add(new ThemeDescriptor("light", "Light",
                    new Uri("pack://application:,,,/ConsumerSmoke;component/Themes/Light.xaml"), true));
                options.Themes.Add(new ThemeDescriptor("dark", "Dark",
                    new Uri("pack://application:,,,/ConsumerSmoke;component/Themes/Dark.xaml")));
            });

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
    /// 退出时停止宿主，验证消费者路径下的生命周期扩展可正常调用。
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
