using System.Reflection;
using Microsoft.Extensions.Hosting;
using WhiteBoard.Framework.Hosting.Extensions;
using WhiteBoard.Framework.Hosting.Plugins;

namespace WhiteBoard.Framework.Hosting.Wpf.Extensions;

/// <summary>WPF 启动期插件发现扩展，自动共享 WPF Hosting 契约程序集。</summary>
public static class WpfStartupPluginExtensions
{
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

    private static void EnsureSharedAssembly(PluginDiscoveryOptions options)
    {
        var assembly = typeof(IViewLocator).Assembly;
        if (!options.AdditionalSharedAssemblies.Contains(assembly))
            options.AdditionalSharedAssemblies.Add(assembly);
    }
}
