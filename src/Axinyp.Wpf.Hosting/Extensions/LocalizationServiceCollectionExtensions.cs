using Microsoft.Extensions.DependencyInjection.Extensions;
using Axinyp.Wpf.Hosting.Abstractions.Localization;
using Axinyp.Wpf.Hosting.Abstractions.Startup;
using Axinyp.Wpf.Hosting.Localization;

namespace Axinyp.Wpf.Hosting.Extensions;

/// <summary>
/// 本地化服务注册扩展。
/// </summary>
public static class LocalizationServiceCollectionExtensions
{
    /// <summary>
    /// 注册基于 ResourceManager 的本地化服务。
    /// 若未显式提供支持文化列表，则自动把默认文化加入支持列表。
    /// </summary>
    public static IServiceCollection AddResxLocalization(this IServiceCollection services, Action<LocalizationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new LocalizationOptions();
        configure(options);

        if (options.SupportedCultures.Count == 0)
            options.SupportedCultures.Add(options.DefaultCulture);

        services.TryAddSingleton(options);
        services.TryAddSingleton<ILocalizationService, LocalizationService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupTask, LocalizationStartupTask>());
        return services;
    }
}
