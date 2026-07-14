using Microsoft.Extensions.DependencyInjection.Extensions;
using WhiteBoard.Framework.Hosting.Settings;

namespace WhiteBoard.Framework.Hosting.Extensions;

public static class SettingsServiceCollectionExtensions
{
    public static IServiceCollection AddWhiteBoardSettings(this IServiceCollection services, Action<SettingsStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new SettingsStoreOptions(); configure?.Invoke(options);
        services.TryAddSingleton(options);
        services.TryAddSingleton<ISettingsStore, JsonSettingsStore>();
        return services;
    }
}
