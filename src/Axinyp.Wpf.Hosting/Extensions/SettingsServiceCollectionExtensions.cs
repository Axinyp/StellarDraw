using Microsoft.Extensions.DependencyInjection.Extensions;
using Axinyp.Wpf.Hosting.Abstractions.Settings;
using Axinyp.Wpf.Hosting.Settings;

namespace Axinyp.Wpf.Hosting.Extensions;

/// <summary>
/// 本地 JSON 设置存储注册扩展。
/// </summary>
public static class SettingsServiceCollectionExtensions
{
    /// <summary>
    /// 注册 JSON 设置存储。
    /// 调用方可通过委托覆盖默认应用名、目录和文件名。
    /// </summary>
    public static IServiceCollection AddJsonSettings(this IServiceCollection services, Action<SettingsStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new SettingsStoreOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);
        services.TryAddSingleton<ISettingsStore, JsonSettingsStore>();
        return services;
    }
}
