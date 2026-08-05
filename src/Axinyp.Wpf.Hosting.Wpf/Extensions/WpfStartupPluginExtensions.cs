using System.Reflection;
using Microsoft.Extensions.Hosting;
using Axinyp.Wpf.Hosting.Extensions;
using Axinyp.Wpf.Hosting.Abstractions.Plugins;
using Axinyp.Wpf.Hosting.Plugins;

namespace Axinyp.Wpf.Hosting.Wpf.Extensions;

/// <summary>
/// WPF 启动期插件发现扩展，自动共享 WPF 宿主契约程序集。
/// 这里共享的不是整个 WPF 默认实现包，而是承载公共契约的程序集，
/// 目的是让插件中的 <see cref="IViewLocator"/>、<see cref="IWpfUiContributionProvider"/> 等接口类型
/// 与宿主进程内看到的是同一份 CLR 类型身份，避免出现“名称相同但无法转换”的问题。
/// </summary>
public static class WpfStartupPluginExtensions
{
    /// <summary>
    /// 从指定目录注册 WPF 启动期插件，并自动共享 WPF 宿主契约程序集。
    /// <para>
    /// 该方法会在原有插件配置前后各执行一次共享程序集注入：
    /// 前一次保证调用方可以在回调中读取到当前默认共享状态；
    /// 后一次则保证即使调用方误清空或覆盖集合，WPF 公共契约程序集仍然会被重新补回。
    /// </para>
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="directory">插件根目录。</param>
    /// <param name="configure">对插件发现选项的附加配置。</param>
    public static HostApplicationBuilder AddWpfStartupPluginsFromDirectory(this HostApplicationBuilder builder,
        string directory, Action<PluginDiscoveryOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return builder.AddStartupPluginsFromDirectory(directory, options =>
        {
            EnsureSharedAssembly(options);
            configure?.Invoke(options);
            EnsureSharedAssembly(options);
        });
    }

    /// <summary>
    /// 确保插件 ALC 会共享承载 WPF 公共契约的程序集。
    /// 注意这里使用 <see cref="IViewLocator"/> 所在程序集作为锚点，
    /// 因为它位于 Abstractions 的 Windows 资产中，能够稳定代表所有 WPF 公共契约边界。
    /// </summary>
    private static void EnsureSharedAssembly(PluginDiscoveryOptions options)
    {
        var assembly = typeof(IViewLocator).Assembly;
        if (!options.AdditionalSharedAssemblies.Contains(assembly))
            options.AdditionalSharedAssemblies.Add(assembly);
    }
}
