using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WhiteBoard.Framework.Hosting.Startup;
using WhiteBoard.Framework.Hosting.Wpf.Localization;

namespace WhiteBoard.Framework.Hosting.Wpf.Extensions;

public static class WpfLocalizationServiceCollectionExtensions
{
    public static IServiceCollection AddWhiteBoardWpfLocalization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<WpfLocalizationBindingSource>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupTask, WpfLocalizationStartupTask>());
        return services;
    }
}
