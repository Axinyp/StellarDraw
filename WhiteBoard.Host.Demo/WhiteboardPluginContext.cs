using System.Reflection;
using System.Runtime.Loader;

namespace WhiteBoard.Host.Demo;

/// <summary>
/// 每个插件实例独享一个可回收 ALC。
/// 精确共享列表：WPF 框架程序集 + 契约程序集（IWhiteboardWpfPlugin / IPluginViewHandle 等
/// 类型必须跨 ALC 边界一致，否则 InvalidCastException）。
/// </summary>
internal sealed class WhiteboardPluginContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public WhiteboardPluginContext(string pluginDllPath)
        : base(name: "WhiteboardPlugin", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginDllPath);
    }

    protected override Assembly? Load(AssemblyName name)
    {
        if (SharedAssemblies.Contains(name.Name ?? string.Empty))
            return null; // 委托给 Default Context，保证类型同一性

        var path = _resolver.ResolveAssemblyToPath(name);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is not null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }

    // WPF 框架 + 所有 Contracts 程序集必须共享，确保类型可跨边界 cast。
    // Core / UI / AI 也共享，使宿主可以直接持有 WhiteboardSession 引用并创建 AI 层。
    private static readonly HashSet<string> SharedAssemblies =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // WPF
            "WindowsBase",
            "PresentationCore",
            "PresentationFramework",
            "System.Xaml",
            // Contracts
            "WhiteBoard.Contracts",
            "WhiteBoard.Contracts.Wpf",
            "WhiteBoard.Protocol",
            // Core / UI / AI（Host 直接引用，必须与 Plugin 共享同一类型标识）
            "WhiteBoard.Core",
            "WhiteBoard.UI",
            "WhiteBoard.AI",
            // BCL / runtime（避免版本冲突）
            "System.Text.Json",
            "System.Runtime",
        };
}
