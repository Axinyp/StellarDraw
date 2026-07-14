using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WhiteBoard.Framework.Hosting.Modules;
using WhiteBoard.Framework.Hosting.Wpf.Internal;
using WhiteBoard.Framework.Hosting.Wpf.Internal.Plugins;
using WhiteBoard.Framework.Hosting.Wpf.Plugins;

namespace WhiteBoard.Framework.Hosting.Wpf.Extensions;

/// <summary>WPF 宿主基础设施与显式 View/VM 映射注册扩展。</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>注册 WPF 视图定位与窗口管理服务。调用方必须在 Host.Build 前注册所有 View 映射。</summary>
    public static IServiceCollection AddWhiteBoardFrameworkWpf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IViewLocator, ViewLocator>();
        services.TryAddSingleton<ViewCompositionFactory>();
        services.TryAddSingleton<IWindowManager, WindowManager>();
        services.TryAddSingleton<RegionManager>();
        services.TryAddSingleton<IRegionManager>(sp => sp.GetRequiredService<RegionManager>());
        services.TryAddSingleton<INavigationService, NavigationService>();
        services.TryAddSingleton<IDialogService, DialogService>();
        services.TryAddSingleton<IWpfUiContributionCatalog, WpfUiContributionCatalog>();
        return services;
    }

    /// <summary>
    /// 显式注册唯一的 ViewModel → View 映射。View 始终为 transient；
    /// ViewModel 的生命周期应由调用方按业务需求单独使用原生 DI 注册。
    /// </summary>
    public static IServiceCollection AddView<TViewModel, TView>(this IServiceCollection services)
        where TViewModel : class
        where TView : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTransient<TView>();
        services.AddSingleton(new ViewRegistration(typeof(TViewModel), typeof(TView)));
        return services;
    }

    /// <summary>注册一个声明式插件 UI Provider；Provider 在 Host Build 后由目录按模块顺序聚合。</summary>
    public static IServiceCollection AddWpfUiContributions<TModule, TProvider>(this IServiceCollection services, int order = 0)
        where TModule : IModule
        where TProvider : class, IWpfUiContributionProvider
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IWpfUiContributionCatalog, WpfUiContributionCatalog>();
        services.TryAddTransient<TProvider>();
        services.AddSingleton(new WpfUiContributionProviderRegistration(typeof(TModule), typeof(TProvider), order));
        return services;
    }
}
