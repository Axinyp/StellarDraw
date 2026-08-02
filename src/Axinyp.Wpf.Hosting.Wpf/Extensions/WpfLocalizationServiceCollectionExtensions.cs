using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Axinyp.Wpf.Hosting.Abstractions.Startup;
using Axinyp.Wpf.Hosting.Abstractions.Wpf;
using Axinyp.Wpf.Hosting.Wpf.Internal;
using Axinyp.Wpf.Hosting.Wpf.Localization;

namespace Axinyp.Wpf.Hosting.Wpf.Extensions;

/// <summary>
/// WPF 动态本地化绑定服务注册扩展。
/// </summary>
public static class WpfLocalizationServiceCollectionExtensions
{
    /// <summary>
    /// 注册 WPF 本地化绑定源，并在宿主启动阶段完成初始绑定刷新。
    /// </summary>
    public static IServiceCollection AddWpfLocalizationBindings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IUiDispatcher, WpfUiDispatcher>();
        services.TryAddSingleton<WpfLocalizationBindingSource>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupTask, WpfLocalizationStartupTask>());
        return services;
    }
}
