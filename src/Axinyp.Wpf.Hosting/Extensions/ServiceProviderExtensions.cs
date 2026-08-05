using Axinyp.Wpf.Hosting.Abstractions.Lifecycle;

namespace Axinyp.Wpf.Hosting.Extensions;

/// <summary>
/// 已构建服务提供程序的兼容性扩展。
/// 新代码应使用 <c>IHost.StartModularHostAsync</c> / <c>StopModularHostAsync</c>，
/// 以确保模块异步生命周期和启动任务由 Generic Host 正确执行。
/// </summary>
public static class ServiceProviderExtensions
{
    /// <summary>
    /// 启动旧版同步模块化宿主入口。
    /// 仅为兼容历史调用方保留；新代码应改用 <c>IHost.StartAsync</c>
    /// 或 <see cref="HostExtensions.StartModularHostAsync(Microsoft.Extensions.Hosting.IHost, CancellationToken)"/>。
    /// </summary>
    /// <param name="provider">根服务提供程序。</param>
    [Obsolete("请使用 IHost.StartModularHostAsync；同步入口不会执行模块生命周期或启动任务。")]
    public static IServiceProvider StartModularHosting(this IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var lifetime = provider.GetRequiredService<IApplicationLifetime>();
        if (lifetime is not Internal.ApplicationLifetime implementation)
            throw new InvalidOperationException("未注册模块化宿主生命周期实现。");

        implementation.MoveTo(ApplicationState.Starting);
        implementation.MoveTo(ApplicationState.Started);
        return provider;
    }

    /// <summary>
    /// 停止旧版同步模块化宿主入口。
    /// 仅为兼容历史调用方保留；新代码应改用 <c>IHost.StopAsync</c>
    /// 或 <see cref="HostExtensions.StopModularHostAsync(Microsoft.Extensions.Hosting.IHost, CancellationToken)"/>。
    /// </summary>
    /// <param name="provider">根服务提供程序。</param>
    [Obsolete("请使用 IHost.StopModularHostAsync；同步入口不会执行模块生命周期。")]
    public static IServiceProvider StopModularHosting(this IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var lifetime = provider.GetRequiredService<IApplicationLifetime>();
        if (lifetime is not Internal.ApplicationLifetime implementation)
            throw new InvalidOperationException("未注册模块化宿主生命周期实现。");

        implementation.MoveTo(ApplicationState.Stopping);
        implementation.MoveTo(ApplicationState.Stopped);
        return provider;
    }
}
