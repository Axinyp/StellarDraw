using Microsoft.Extensions.Hosting;

namespace WhiteBoard.Framework.Hosting.Extensions;

/// <summary>
/// <see cref="IHost"/> 的框架生命周期扩展。
/// 正常情况下直接调用 Generic Host 的 StartAsync/StopAsync 即可；这两个方法用于表达
/// WhiteBoard 框架的启动意图，并避免宿主再调用已废弃的同步 IServiceProvider 扩展。
/// </summary>
public static class HostExtensions
{
    public static Task StartWhiteBoardFrameworkAsync(this IHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.StartAsync(cancellationToken);
    }

    public static Task StopWhiteBoardFrameworkAsync(this IHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.StopAsync(cancellationToken);
    }
}
