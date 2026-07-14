using Microsoft.Extensions.Hosting;
using WhiteBoard.Framework.Hosting.Internal.Plugins;
using WhiteBoard.Framework.Hosting.Plugins;

namespace WhiteBoard.Framework.Hosting.Extensions;

/// <summary>HostApplicationBuilder 的启动期插件发现扩展。</summary>
public static class HostApplicationBuilderPluginExtensions
{
    /// <summary>
    /// 扫描指定根目录中的插件子目录。发现和模块注册均发生在 Host.Build 前；
    /// 无效插件只记录到 <see cref="IPluginCatalog"/>，不会阻断其他插件或宿主启动。
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
        builder.AddWhiteBoardFramework();

        var options = new PluginDiscoveryOptions();
        configure(options);
        var (catalog, runtime) = StartupPluginDiscoverer.Discover(options);
        var state = StartupPluginStateStore.GetOrCreate(builder);
        state.Add(catalog, runtime);
        foreach (var module in runtime.Modules)
            builder.AddModule(module);
        return builder;
    }
}
