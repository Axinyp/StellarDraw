using System.Runtime.CompilerServices;
using Microsoft.Extensions.Hosting;

namespace Axinyp.Wpf.Hosting.Internal.Plugins;

/// <summary>以弱引用绑定 HostApplicationBuilder 与其启动期插件状态。</summary>
internal static class StartupPluginStateStore
{
    private static readonly ConditionalWeakTable<HostApplicationBuilder, StartupPluginState> States = new();

    public static StartupPluginState GetOrCreate(HostApplicationBuilder builder)
        => States.GetValue(builder, _ => new StartupPluginState());

    public static bool TryGet(HostApplicationBuilder builder, out StartupPluginState? state)
        => States.TryGetValue(builder, out state);
}
