using Microsoft.Extensions.Hosting;

namespace Axinyp.Wpf.Hosting.Extensions;

/// <summary>
/// <see cref="IHost"/> 的模块化宿主生命周期扩展。
/// 正常情况下直接调用 Generic Host 的 StartAsync/StopAsync 即可；
/// 这里保留语义化别名，避免上层继续依赖已废弃的同步 IServiceProvider 扩展。
/// </summary>
public static class HostExtensions
{
    /// <summary>
    /// 启动模块化宿主。
    /// 语义上等价于 <see cref="IHost.StartAsync(CancellationToken)"/>，
    /// 仅用于让上层代码表达“启动的是模块化宿主”这一意图。
    /// </summary>
    /// <param name="host">已构建的宿主实例。</param>
    /// <param name="cancellationToken">启动取消令牌。</param>
    public static Task StartModularHostAsync(this IHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.StartAsync(cancellationToken);
    }

    /// <summary>
    /// 停止模块化宿主。
    /// 语义上等价于 <see cref="IHost.StopAsync(CancellationToken)"/>，
    /// 用于与 <see cref="StartModularHostAsync(IHost, CancellationToken)"/> 成对出现。
    /// </summary>
    /// <param name="host">已启动的宿主实例。</param>
    /// <param name="cancellationToken">停止取消令牌。</param>
    public static Task StopModularHostAsync(this IHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.StopAsync(cancellationToken);
    }
}
