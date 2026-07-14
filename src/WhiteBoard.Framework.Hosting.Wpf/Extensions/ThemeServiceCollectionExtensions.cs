using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WhiteBoard.Framework.Hosting.Startup;
using WhiteBoard.Framework.Hosting.Wpf.Themes;

namespace WhiteBoard.Framework.Hosting.Wpf.Extensions;

public static class ThemeServiceCollectionExtensions
{
    public static IServiceCollection AddWhiteBoardWpfThemes(this IServiceCollection services, Action<ThemeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(configure);
        var options = new ThemeOptions(); configure(options);
        if (options.Themes.Count == 0) throw new ArgumentException("至少需要注册一个主题。", nameof(configure));
        if (options.Themes.GroupBy(theme => theme.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new ArgumentException("主题 Id 不可重复。", nameof(configure));
        services.TryAddSingleton(options);
        services.TryAddSingleton<IThemeService, ResourceDictionaryThemeService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupTask, ThemeStartupTask>());
        return services;
    }
}
