using System.Reflection;
using System.Runtime.Loader;
using Axinyp.Wpf.Hosting.Abstractions.Modules;

namespace Axinyp.Wpf.Hosting.Internal.Plugins;

/// <summary>
/// 每个启动期插件独享的可回收加载上下文。
/// 它的目标不是隔离“一切程序集”，而是：
/// <list type="bullet">
/// <item><description>让插件自己的依赖在独立 ALC 中加载，避免污染默认加载上下文；</description></item>
/// <item><description>让框架公共契约程序集继续从 Default ALC 共享，保证宿主与插件之间的接口类型可互相转换；</description></item>
/// <item><description>在 Host 释放时支持整体回收，避免长期运行后残留不可卸载程序集。</description></item>
/// </list>
/// </summary>
internal sealed class StartupPluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly IReadOnlyDictionary<string, Assembly> _sharedAssemblies;

    /// <summary>
    /// 创建一个绑定到指定插件入口程序集的可回收加载上下文。
    /// </summary>
    /// <param name="entryAssemblyPath">插件入口程序集的完整路径。</param>
    /// <param name="additionalSharedAssemblies">除核心宿主契约外，还需要显式共享给插件的程序集集合。</param>
    public StartupPluginLoadContext(string entryAssemblyPath, IEnumerable<Assembly> additionalSharedAssemblies)
        : base($"WhiteBoard.Plugin:{Path.GetFileNameWithoutExtension(entryAssemblyPath)}", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
        _sharedAssemblies = BuildSharedAssemblies(additionalSharedAssemblies);
    }

    /// <summary>
    /// 先尝试从共享程序集表中命中公共契约；若未命中，再回退到当前插件目录的依赖解析。
    /// 这样既能保证类型身份统一，又能保持插件自己的第三方依赖局部化。
    /// </summary>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name ?? string.Empty;
        if (_sharedAssemblies.TryGetValue(name, out var shared))
            return shared;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    /// <summary>
    /// 解析插件所需的本机依赖库。
    /// </summary>
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }

    /// <summary>
    /// 构建共享程序集字典。
    /// 至少会共享：
    /// <list type="bullet">
    /// <item><description><see cref="IModule"/> 所在程序集，用于模块接口类型统一；</description></item>
    /// <item><description><see cref="IConfiguration"/> 与 <see cref="IServiceCollection"/> 所在程序集，用于配置和 DI 基元统一；</description></item>
    /// <item><description>调用方显式补充的共享程序集，例如 WPF 契约所在的 Abstractions Windows 资产。</description></item>
    /// </list>
    /// </summary>
    private static IReadOnlyDictionary<string, Assembly> BuildSharedAssemblies(
        IEnumerable<Assembly> additionalSharedAssemblies)
    {
        var assemblies = new[]
        {
            typeof(IModule).Assembly,
            typeof(IConfiguration).Assembly,
            typeof(IServiceCollection).Assembly
        }.Concat(additionalSharedAssemblies);

        return assemblies
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.GetName().Name))
            .GroupBy(assembly => assembly.GetName().Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }
}
