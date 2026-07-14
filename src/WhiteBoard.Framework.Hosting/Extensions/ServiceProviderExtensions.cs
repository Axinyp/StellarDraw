using WhiteBoard.Framework.Hosting.Lifecycle;

namespace WhiteBoard.Framework.Hosting.Extensions;

/// <summary>
/// 已构建服务提供程序的兼容性扩展。
/// 新代码应使用 IHost.StartWhiteBoardFrameworkAsync / StopWhiteBoardFrameworkAsync，
/// 以确保模块异步生命周期和启动任务由 Generic Host 正确执行。
/// </summary>
public static class ServiceProviderExtensions
{
    [Obsolete("请使用 IHost.StartWhiteBoardFrameworkAsync；同步入口不会执行模块生命周期或启动任务。")]
    public static IServiceProvider StartWhiteBoardFramework(this IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var lifetime = provider.GetRequiredService<IApplicationLifetime>();
        if (lifetime is not Internal.ApplicationLifetime implementation)
            throw new InvalidOperationException("未注册 WhiteBoard Framework 生命周期实现。");

        implementation.MoveTo(ApplicationState.Starting);
        implementation.MoveTo(ApplicationState.Started);
        return provider;
    }

    [Obsolete("请使用 IHost.StopWhiteBoardFrameworkAsync；同步入口不会执行模块生命周期。")]
    public static IServiceProvider StopWhiteBoardFramework(this IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var lifetime = provider.GetRequiredService<IApplicationLifetime>();
        if (lifetime is not Internal.ApplicationLifetime implementation)
            throw new InvalidOperationException("未注册 WhiteBoard Framework 生命周期实现。");

        implementation.MoveTo(ApplicationState.Stopping);
        implementation.MoveTo(ApplicationState.Stopped);
        return provider;
    }
}
