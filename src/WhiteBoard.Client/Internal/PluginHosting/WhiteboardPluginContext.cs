using System.Reflection;
using System.Runtime.Loader;

namespace WhiteBoard.Client.Internal.PluginHosting;

/// <summary>
/// WhiteBoard.Client 首轮真实业务回接使用的插件加载上下文。
/// 它直接复用 Demo 中已经验证过的“独立 ALC + 共享关键程序集”的策略，
/// 目的不是做完整插件系统，而是先稳定验证：
/// 1. Client Shell 能否加载真实白板插件；
/// 2. 插件中的白板视图能否嵌入正式宿主窗口；
/// 3. 宿主与插件之间的 Contracts / Core / UI 类型能否保持一致。
/// </summary>
internal sealed class WhiteboardPluginContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    /// <summary>
    /// 创建绑定到指定插件 DLL 的可回收加载上下文。
    /// 每个插件实例独占一个 ALC，便于在关闭应用或后续热加载试验时完整卸载。
    /// </summary>
    /// <param name="pluginDllPath">插件主 DLL 的完整路径。</param>
    public WhiteboardPluginContext(string pluginDllPath)
        : base(name: "WhiteboardPlugin", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginDllPath);
    }

    /// <summary>
    /// 解析托管程序集。
    /// 共享程序集必须回退到 Default Context，以确保宿主与插件看到的是同一份 CLR 类型身份；
    /// 否则看似相同名称的接口（如 <c>IWhiteboardWpfPlugin</c>、<c>IPluginViewHandle</c>、<c>WhiteboardSession</c>）
    /// 会因为来自不同 ALC 而无法强制转换。
    /// </summary>
    protected override Assembly? Load(AssemblyName name)
    {
        if (SharedAssemblies.Contains(name.Name ?? string.Empty))
            return null;

        var path = _resolver.ResolveAssemblyToPath(name);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }

    /// <summary>
    /// 解析插件依赖的本机库。
    /// </summary>
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is not null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }

    /// <summary>
    /// 首轮回接所需共享程序集集合。
    /// 这里故意共享：
    /// 1. WPF 框架程序集；
    /// 2. Contracts / Contracts.Wpf / Protocol；
    /// 3. WhiteBoard.Core / WhiteBoard.UI；
    /// 这样插件返回的视图、宿主侧看到的接口，以及白板业务对象都能跨边界安全转换。
    /// 当前不把 WhiteBoard.AI 放入首轮共享集合，因为本轮明确不接入 AI/MCP 路径。
    /// </summary>
    private static readonly HashSet<string> SharedAssemblies =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "WindowsBase",
            "PresentationCore",
            "PresentationFramework",
            "System.Xaml",
            "WhiteBoard.Contracts",
            "WhiteBoard.Contracts.Wpf",
            "WhiteBoard.Protocol",
            "WhiteBoard.Core",
            "WhiteBoard.UI",
            "System.Text.Json",
            "System.Runtime",
        };
}
