using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Axinyp.Wpf.Hosting.Abstractions.Startup;
using Axinyp.Wpf.Hosting.Abstractions.Wpf.Themes;
using Axinyp.Wpf.Hosting.Wpf.Themes;

namespace Axinyp.Wpf.Hosting.Wpf.Extensions;

/// <summary>
/// 主题服务注册扩展。
/// 该扩展只负责装配“主题切换所需的默认服务”，
/// 不负责规定调用方必须有哪些主题、也不负责规定主题资源字典内部如何组织样式。
/// </summary>
public static class ThemeServiceCollectionExtensions
{
    /// <summary>
    /// 注册 WPF 主题服务，并在宿主启动时应用默认主题。
    /// <para>
    /// 配置委托必须至少提供一个主题；否则默认实现无法确定初始主题来源。
    /// 同时框架会显式拒绝重复的主题 Id，避免后续按 Id 切换时出现歧义。
    /// </para>
    /// <para>
    /// 这里注册的 <see cref="ThemeOptions"/> 为单例配置对象，
    /// <see cref="ResourceDictionaryThemeService"/> 负责真正切换 <see cref="System.Windows.Application.Resources"/> 中的主题字典，
    /// <see cref="ThemeStartupTask"/> 则负责在 Host 启动阶段恢复用户上次选择的主题或回退到默认主题。
    /// </para>
    /// </summary>
    public static IServiceCollection AddWpfThemes(this IServiceCollection services, Action<ThemeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ThemeOptions();
        configure(options);

        if (options.Themes.Count == 0)
            throw new ArgumentException("至少需要注册一个主题。", nameof(configure));

        if (options.Themes.GroupBy(theme => theme.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new ArgumentException("主题 Id 不可重复。", nameof(configure));

        services.TryAddSingleton(options);
        services.TryAddSingleton<IThemeService, ResourceDictionaryThemeService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupTask, ThemeStartupTask>());
        return services;
    }
}
