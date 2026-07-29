using System.Runtime.CompilerServices;
using Microsoft.Extensions.Hosting;
using Axinyp.Wpf.Hosting.Internal;
using Axinyp.Wpf.Hosting.Internal.Plugins;
using Axinyp.Wpf.Hosting.Abstractions.Modules;
using Axinyp.Wpf.Hosting.Abstractions.Plugins;

namespace Axinyp.Wpf.Hosting.Extensions;

/// <summary>
/// <see cref="HostApplicationBuilder"/> 上的模块化宿主装配扩展。
/// 这一层只负责“构建前收集信息”：
/// <list type="number">
/// <item><description>缓存待注册模块；</description></item>
/// <item><description>在最终 Build 前统一做依赖排序与重复校验；</description></item>
/// <item><description>把排序后的模块按稳定顺序写回 DI 容器；</description></item>
/// <item><description>必要时把启动期插件发现结果一起并入最终宿主。</description></item>
/// </list>
/// 这样可以避免调用方在多个位置分散注册模块时，顺序受调用时机影响而变得不可预测。
/// </summary>
public static class HostApplicationBuilderExtensions
{
    // HostApplicationBuilder 没有公开 Properties；这里用弱表保存暂存模块，
    // 既避免额外包装 builder，也避免因为静态字典强引用而延长 builder 生命周期。
    private static readonly ConditionalWeakTable<HostApplicationBuilder, ModuleRegistry> ModuleRegistries = new();

    /// <summary>
    /// 注册模块化宿主核心服务并初始化模块注册表。
    /// 该方法本身不会立即执行模块的 <c>ConfigureServices</c>，
    /// 只是在 builder 上建立“稍后统一装配”的前置条件。
    /// </summary>
    public static HostApplicationBuilder AddModularHosting(this HostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddModularHosting();
        _ = GetOrCreateModuleRegistry(builder);
        return builder;
    }

    /// <summary>
    /// 暂存一个无参构造模块。
    /// 这里只记录“有哪些模块”，不立刻调用模块代码；
    /// 真正的依赖排序、重复校验和服务注册会在 <see cref="BuildModularHost"/> 时统一完成。
    /// </summary>
    public static HostApplicationBuilder AddModule<TModule>(this HostApplicationBuilder builder)
        where TModule : IModule, new()
        => builder.AddModule(new TModule());

    /// <summary>
    /// 暂存一个模块实例。
    /// 模块必须在 Host.Build 前加入，不能在运行时再修改容器，
    /// 否则会破坏 Generic Host 已构建完成后的服务一致性与生命周期假设。
    /// </summary>
    public static HostApplicationBuilder AddModule(this HostApplicationBuilder builder, IModule module)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(module);
        GetOrCreateModuleRegistry(builder).Add(module);
        return builder;
    }

    /// <summary>
    /// 完成模块装配并构建 Generic Host。
    /// 调用流程固定为：
    /// <list type="number">
    /// <item><description>补齐模块化宿主核心服务；</description></item>
    /// <item><description>取出此前暂存的模块集合；</description></item>
    /// <item><description>执行依赖校验、稳定拓扑排序，并按顺序调用每个模块的 <c>ConfigureServices</c>；</description></item>
    /// <item><description>把冻结后的 <see cref="IModuleCatalog"/> 写入容器，供运行时生命周期与诊断层读取；</description></item>
    /// <item><description>若存在启动期插件状态，则把插件目录结果并入最终宿主并返回可感知插件生命周期的 Host 包装器。</description></item>
    /// </list>
    /// 该方法是“模块世界”进入“运行时世界”的唯一入口，调用后不应再继续追加模块或插件。
    /// </summary>
    public static IHost BuildModularHost(this HostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddModularHosting();

        var modules = GetOrCreateModuleRegistry(builder)
            .Materialize(builder.Services, builder.Configuration);
        builder.Services.Replace(ServiceDescriptor.Singleton<IModuleCatalog>(new ModuleCatalog(modules)));

        if (StartupPluginStateStore.TryGet(builder, out var pluginState) && pluginState is not null)
        {
            builder.Services.Replace(ServiceDescriptor.Singleton<IPluginCatalog>(pluginState.CreateCatalog()));
            return new PluginAwareHost(builder.Build(), pluginState);
        }

        return builder.Build();
    }

    private static ModuleRegistry GetOrCreateModuleRegistry(HostApplicationBuilder builder)
        => ModuleRegistries.GetValue(builder, _ => new ModuleRegistry());
}
