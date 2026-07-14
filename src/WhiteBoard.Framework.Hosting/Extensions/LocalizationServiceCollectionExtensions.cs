using Microsoft.Extensions.DependencyInjection.Extensions;
using WhiteBoard.Framework.Hosting.Localization;
using WhiteBoard.Framework.Hosting.Startup;

namespace WhiteBoard.Framework.Hosting.Extensions;

public static class LocalizationServiceCollectionExtensions
{
    public static IServiceCollection AddWhiteBoardLocalization(this IServiceCollection services, Action<LocalizationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(configure);
        var options = new LocalizationOptions(); configure(options);
        if (options.SupportedCultures.Count == 0) options.SupportedCultures.Add(options.DefaultCulture);
        services.TryAddSingleton(options);
        services.TryAddSingleton<ILocalizationService, LocalizationService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Startup.IStartupTask, LocalizationStartupTask>());
        return services;
    }
}
