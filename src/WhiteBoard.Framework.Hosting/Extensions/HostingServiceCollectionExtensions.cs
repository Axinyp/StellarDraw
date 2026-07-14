using Microsoft.Extensions.Hosting;
using WhiteBoard.Framework.Hosting.Internal;
using WhiteBoard.Framework.Hosting.Lifecycle;
using WhiteBoard.Framework.Hosting.Modules;

namespace WhiteBoard.Framework.Hosting.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> 上的框架注册扩展。
/// </summary>
public static class HostingServiceCollectionExtensions
{
    /// <summary>
    /// 注册 WhiteBoard 框架核心服务。幂等，可安全多次调用。
    /// 模块目录由 BuildWhiteBoardFrameworkHost 在 Build 前写入，初始空目录使无模块宿主也可运行。
    /// </summary>
    public static IServiceCollection AddWhiteBoardFramework(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ApplicationLifetime>();
        services.TryAddSingleton<Lifecycle.IApplicationLifetime>(sp => sp.GetRequiredService<ApplicationLifetime>());
        services.TryAddSingleton<IModuleCatalog>(new ModuleCatalog([]));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FrameworkHostedLifecycleService>());
        return services;
    }

    /// <summary>
    /// 立即执行模块服务注册的低层入口。它不参与全局依赖排序，也没有模块运行时生命周期；
    /// 常规宿主应使用 HostApplicationBuilder.AddModule + BuildWhiteBoardFrameworkHost。
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
    /// 检查单例是否俘获了作用域服务（captive dependency）。返回问题描述列表，空表示无问题。
    /// 供宿主在构建前主动校验；不抛异常，由调用方决定如何处置。
    /// </summary>
    public static IReadOnlyList<string> ValidateServiceLifetimes(this IServiceCollection services)
        => ServiceLifetimeValidator.Validate(services).Select(i => i.ToString()).ToList();
}
