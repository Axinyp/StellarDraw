using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Axinyp.Wpf.Hosting.Abstractions.Modules;
using Axinyp.Wpf.Hosting.Wpf.Internal;
using Axinyp.Wpf.Hosting.Wpf.Internal.Plugins;
using Axinyp.Wpf.Hosting.Abstractions.Wpf.Plugins;

namespace Axinyp.Wpf.Hosting.Wpf.Extensions;

/// <summary>
/// WPF 宿主基础设施与显式 View/VM 映射注册扩展。
/// 这一层只处理“表示层基础设施”的通用装配，不负责任何业务窗口、业务导航或业务菜单定义。
/// 调用方应先注册自己的 ViewModel，再显式声明需要暴露给框架的 View、Region、Theme 与 UI Contribution。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 WPF 表示层所需的核心服务。
    /// 包括：
    /// <list type="bullet">
    /// <item><description><see cref="IViewLocator"/>：根据 ViewModel 类型定位 View 类型；</description></item>
    /// <item><description><see cref="IWindowManager"/>：负责窗口创建、Owner 校验与 scope 清理；</description></item>
    /// <item><description><see cref="IRegionManager"/> / <see cref="INavigationService"/>：负责区域注册与区域导航；</description></item>
    /// <item><description><see cref="IDialogService"/>：负责基于 ViewModel 的模态对话框；</description></item>
    /// <item><description><see cref="IWpfUiContributionCatalog"/>：聚合插件声明式 UI 元数据。</description></item>
    /// </list>
    /// 这些服务只注册一次即可；方法内部使用 <c>TryAdd*</c>，便于调用方在必要时覆写默认实现。
    /// </summary>
    public static IServiceCollection AddWpfPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IUiDispatcher, WpfUiDispatcher>();
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
    /// 显式注册唯一的 ViewModel → View 映射。
    /// 框架故意不使用命名约定自动扫描，原因是：
    /// <list type="bullet">
    /// <item><description>减少大型项目中误匹配与静默覆盖的风险；</description></item>
    /// <item><description>让插件边界与模块边界更明确；</description></item>
    /// <item><description>当映射缺失时，能够给出可诊断、可复现的异常，而不是模糊回退。</description></item>
    /// </list>
    /// <para>
    /// View 始终注册为 transient，确保每次显示窗口、导航页面或对话框时都能得到独立视觉实例；
    /// ViewModel 生命周期则由调用方根据业务场景自行决定，可为 transient、scoped 或 singleton。
    /// </para>
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

    /// <summary>
    /// 注册一个声明式插件 UI Provider。
    /// Provider 本身不直接操作宿主菜单、工具栏或区域控件，
    /// 它只负责返回一份稳定、可排序、可诊断的“UI 元数据声明”。
    /// 这些声明会在 Host Build 后统一聚合进冻结目录中，随后由上层 Shell 自主决定如何渲染。
    /// </summary>
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
