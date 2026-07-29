using System.Reflection;
using System.Runtime.Loader;

namespace StellarDraw.Axinyp.Spike;

/// <summary>
/// 为 legacy 白板插件创建可回收程序集加载上下文。
/// 该实现只服务于 Spike 的兼容试验，不属于 Axinyp 插件模型，也不是安全隔离边界。
/// </summary>
internal sealed class LegacyWhiteboardPluginLoadContext : AssemblyLoadContext
{
    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsBase",
        "PresentationCore",
        "PresentationFramework",
        "System.Xaml",
        "WhiteBoard.Contracts",
        "WhiteBoard.Contracts.Wpf",
        "System.Text.Json",
        "System.Runtime",
    };

    private readonly AssemblyDependencyResolver _resolver;

    /// <summary>
    /// 创建绑定到已验证的 legacy plugin 主程序集路径的加载上下文。
    /// </summary>
    /// <param name="pluginAssemblyPath">legacy plugin 主程序集绝对路径。</param>
    public LegacyWhiteboardPluginLoadContext(string pluginAssemblyPath)
        : base("StellarDraw.LegacyWhiteboardPlugin", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginAssemblyPath);
    }

    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (SharedAssemblies.Contains(assemblyName.Name ?? string.Empty))
            return null;

        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
    }

    /// <inheritdoc />
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(libraryPath);
    }
}
