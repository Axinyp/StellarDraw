using Microsoft.Extensions.Hosting;
using Axinyp.Wpf.Hosting.Internal;
using Axinyp.Wpf.Hosting.Abstractions.Lifecycle;
using Axinyp.Wpf.Hosting.Abstractions.Modules;

namespace Axinyp.Wpf.Hosting.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> 上的模块化宿主注册扩展。
/// </summary>
public static class HostingServiceCollectionExtensions
{
    /// <summary>
    /// 注册模块化宿主核心服务。该方法幂等，可安全多次调用。
    /// 模块目录会在 <c>BuildModularHost</c> 前被替换，因此即使没有模块，宿主也能正常运行。
    /// </summary>
    public static IServiceCollection AddModularHosting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ApplicationLifetime>();
        services.TryAddSingleton<Axinyp.Wpf.Hosting.Abstractions.Lifecycle.IApplicationLifetime>(
            sp => sp.GetRequiredService<ApplicationLifetime>());
        services.TryAddSingleton<IModuleCatalog>(new ModuleCatalog([]));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FrameworkHostedLifecycleService>());
        return services;
    }

    /// <summary>
    /// 立即执行模块服务注册的低层入口。
    /// 它不参与全局依赖排序，也不自动接入模块运行时生命周期；
    /// 常规宿主应优先使用 <c>HostApplicationBuilder.AddModule</c> + <c>BuildModularHost</c>。
    /// </summary>
    public static IServiceCollection AddModule(this IServiceCollection services,
        IConfiguration configuration, IModule module)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(module);
        module.ConfigureServices(services, configuration);
        return services;
    }

    /// <summary>
    /// 检查单例是否俘获了作用域服务（captive dependency）。
    /// 返回问题描述列表；空集合表示未发现问题。
    /// </summary>
    public static IReadOnlyList<string> ValidateServiceLifetimes(this IServiceCollection services)
        => ServiceLifetimeValidator.Validate(services).Select(i => i.ToString()).ToList();
}
