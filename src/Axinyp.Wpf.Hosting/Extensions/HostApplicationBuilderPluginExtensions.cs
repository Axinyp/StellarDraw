using Microsoft.Extensions.Hosting;
using Axinyp.Wpf.Hosting.Internal.Plugins;
using Axinyp.Wpf.Hosting.Abstractions.Plugins;
using Axinyp.Wpf.Hosting.Plugins;

namespace Axinyp.Wpf.Hosting.Extensions;

/// <summary>HostApplicationBuilder 的启动期插件发现扩展。</summary>
public static class HostApplicationBuilderPluginExtensions
{
    /// <summary>
    /// 扫描指定根目录中的插件子目录。发现和模块注册均发生在 Host.Build 前。
    /// <para>
    /// 失败隔离边界：清单校验、依赖解析与程序集装载阶段的失败只记录到
    /// <see cref="IPluginCatalog"/>，不阻断其他插件或宿主启动。
    /// 已成功装载模块的 <c>ConfigureServices</c> 与运行时生命周期（Initialize/Start/Stop）
    /// 与宿主内置模块同等对待：其失败会使宿主构建或启动失败——启动期插件是受信代码，
    /// 装配阶段的异常意味着部署本身已损坏，静默跳过只会把故障推迟到更难诊断的运行期。
    /// </para>
    /// </summary>
    public static HostApplicationBuilder AddStartupPluginsFromDirectory(this HostApplicationBuilder builder,
        string pluginDirectory, Action<PluginDiscoveryOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);
        return builder.AddStartupPlugins(options =>
        {
            options.PluginDirectories.Add(pluginDirectory);
            configure?.Invoke(options);
        });
    }

    /// <summary>发现启动期插件并把其模块暂存到现有 ModuleRegistry。</summary>
    public static HostApplicationBuilder AddStartupPlugins(this HostApplicationBuilder builder,
        Action<PluginDiscoveryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        builder.AddModularHosting();

        var options = new PluginDiscoveryOptions();
        configure(options);
        var (catalog, runtime, diagnostics) = StartupPluginDiscoverer.Discover(options);
        var state = StartupPluginStateStore.GetOrCreate(builder);
        var acceptedModules = state.Add(catalog, runtime, diagnostics);
        foreach (var module in acceptedModules)
            builder.AddModule(module);
        return builder;
    }
}
