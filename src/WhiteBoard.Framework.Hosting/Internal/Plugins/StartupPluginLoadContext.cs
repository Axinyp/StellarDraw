using System.Reflection;
using System.Runtime.Loader;

namespace WhiteBoard.Framework.Hosting.Internal.Plugins;

/// <summary>每个启动期插件独享的可回收加载上下文。框架契约程序集从 Default ALC 共享以保证类型可转换。</summary>
internal sealed class StartupPluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly IReadOnlyDictionary<string, Assembly> _sharedAssemblies;

    public StartupPluginLoadContext(string entryAssemblyPath, IEnumerable<Assembly> additionalSharedAssemblies)
        : base($"WhiteBoard.Plugin:{Path.GetFileNameWithoutExtension(entryAssemblyPath)}", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
        _sharedAssemblies = BuildSharedAssemblies(additionalSharedAssemblies);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name ?? string.Empty;
        if (_sharedAssemblies.TryGetValue(name, out var shared))
            return shared;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }

    private static IReadOnlyDictionary<string, Assembly> BuildSharedAssemblies(
        IEnumerable<Assembly> additionalSharedAssemblies)
    {
        var assemblies = new[]
        {
            typeof(Modules.IModule).Assembly,
            typeof(IConfiguration).Assembly,
            typeof(IServiceCollection).Assembly
        }.Concat(additionalSharedAssemblies);

        return assemblies
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.GetName().Name))
            .GroupBy(assembly => assembly.GetName().Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }
}
