using System.Runtime.CompilerServices;
using Microsoft.Extensions.Hosting;
using WhiteBoard.Framework.Hosting.Internal;
using WhiteBoard.Framework.Hosting.Internal.Plugins;
using WhiteBoard.Framework.Hosting.Modules;
using WhiteBoard.Framework.Hosting.Plugins;

namespace WhiteBoard.Framework.Hosting.Extensions;

/// <summary>
/// <see cref="HostApplicationBuilder"/> 上的框架装配扩展（宿主侧首选入口）。
/// 模块会暂存到构建阶段统一排序；必须以 <see cref="BuildWhiteBoardFrameworkHost"/> 完成构建。
/// </summary>
public static class HostApplicationBuilderExtensions
{
    // HostApplicationBuilder 没有公开 Properties；弱表在不延长 builder 生命周期的前提下保存暂存模块。
    private static readonly ConditionalWeakTable<HostApplicationBuilder, ModuleRegistry> ModuleRegistries = new();

    /// <summary>注册 WhiteBoard 框架核心服务并初始化模块注册表。</summary>
    public static HostApplicationBuilder AddWhiteBoardFramework(this HostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddWhiteBoardFramework();
        _ = GetOrCreateModuleRegistry(builder);
        return builder;
    }

    /// <summary>暂存一个无参构造模块；在 BuildWhiteBoardFrameworkHost 时按依赖顺序注册服务。</summary>
    public static HostApplicationBuilder AddModule<TModule>(this HostApplicationBuilder builder)
        where TModule : IModule, new()
        => builder.AddModule(new TModule());

    /// <summary>暂存一个模块实例。模块必须在 Host.Build 前加入，不能在运行时动态修改容器。</summary>
    public static HostApplicationBuilder AddModule(this HostApplicationBuilder builder, IModule module)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(module);
        GetOrCreateModuleRegistry(builder).Add(module);
        return builder;
    }

    /// <summary>
    /// 完成模块装配并构建 Generic Host。先验证模块依赖并稳定拓扑排序，再执行各模块
    /// ConfigureServices；因此这是使用 AddModule 的唯一合法构建入口。
    /// </summary>
    public static IHost BuildWhiteBoardFrameworkHost(this HostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddWhiteBoardFramework();

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
