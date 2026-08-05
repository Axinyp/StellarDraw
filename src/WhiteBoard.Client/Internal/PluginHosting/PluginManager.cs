using System.Reflection;
using System.Runtime.Loader;
using WhiteBoard.Contracts.Plugin;
using WhiteBoard.Contracts.Wpf;

namespace WhiteBoard.Client.Internal.PluginHosting;

/// <summary>
/// 管理单个白板插件的完整生命周期：加载 → 初始化 → 创建视图 → 启动 → 停止 → 卸载。
/// 这是 WhiteBoard.Client 首轮回接中最关键的桥接器：
/// 它让正式 Client Shell 可以复用 `WhiteBoard.Plugin` 内已经验证过的真实白板业务链路，
/// 而不必在宿主中重新实现 Session、ViewModel 与 WPF 组合逻辑。
/// </summary>
internal sealed class PluginManager : IAsyncDisposable
{
    private WhiteboardPluginContext? _context;
    private WeakReference<AssemblyLoadContext>? _weakContext;
    private IWhiteboardWpfPlugin? _plugin;
    private IPluginViewHandle? _viewHandle;
    private CancellationTokenSource? _cts;

    internal IPluginViewHandle? ViewHandle => _viewHandle;

    /// <summary>
    /// 从指定路径加载插件 DLL，完成初始化、创建视图与启动。
    /// <para>
    /// 调用方必须在 UI 线程调用该方法，
    /// 因为插件的 <see cref="IWhiteboardWpfPlugin.CreateView"/> 最终会创建真实 WPF 控件树。
    /// </para>
    /// <para>
    /// 这里刻意沿用 Demo 已验证过的顺序：
    /// 先 <c>InitializeAsync</c>，再 <c>CreateView</c>，最后 <c>StartAsync</c>，
    /// 防止插件在业务对象尚未准备完成时就提前暴露可交互视图。
    /// </para>
    /// </summary>
    public async Task LoadAsync(
        string pluginDllPath,
        IPluginMessageBus bus,
        IHostCapabilities host,
        CancellationToken ct = default)
    {
        if (_plugin is not null)
            throw new InvalidOperationException("A plugin is already loaded. Call UnloadAsync first.");

        Console.WriteLine($"[Client:Plugin] Begin loading: {pluginDllPath}");
        System.Diagnostics.Debug.WriteLine($"[Client:Plugin] Begin loading: {pluginDllPath}");

        var context = new WhiteboardPluginContext(pluginDllPath);
        _context = context;
        _weakContext = new WeakReference<AssemblyLoadContext>(context);

        IWhiteboardWpfPlugin? plugin = null;
        try
        {
            var asm = context.LoadFromAssemblyPath(pluginDllPath);
            plugin = FindPlugin(asm);
            Console.WriteLine($"[Client:Plugin] Resolved plugin type: {plugin.GetType().FullName}");

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            await plugin.InitializeAsync(bus, host, _cts.Token);
            Console.WriteLine($"[Client:Plugin] InitializeAsync completed: {plugin.PluginId}");

            _viewHandle = plugin.CreateView();
            Console.WriteLine($"[Client:Plugin] CreateView completed: {plugin.PluginId}");

            await plugin.StartAsync(_cts.Token);
            Console.WriteLine($"[Client:Plugin] StartAsync completed: {plugin.PluginId}");

            _plugin = plugin;
        }
        catch
        {
            // 只要加载链路任一步失败，都要尽量回收已创建对象，
            // 否则首轮回接阶段最容易留下“Host 能启动，但插件半初始化残留”的隐性状态。
            if (_viewHandle is not null)
            {
                await _viewHandle.DisposeAsync();
                _viewHandle = null;
            }

            if (plugin is not null)
                await plugin.DisposeAsync();

            _cts?.Dispose();
            _cts = null;
            _context?.Unload();
            _context = null;
            throw;
        }
    }

    /// <summary>
    /// 安全卸载插件。
    /// 必须严格遵守“停止业务 → 脱离视图 → 释放句柄 → 释放插件 → 卸载 ALC”的顺序，
    /// 否则 WPF 视觉树、DataContext 或事件引用可能继续持有插件对象，导致可回收 ALC 无法真正被 GC 回收。
    /// </summary>
    public async Task UnloadAsync()
    {
        if (_plugin is null) return;

        Console.WriteLine($"[Client:Plugin] Begin unload: {_plugin.PluginId}");
        _cts?.Cancel();

        await _plugin.StopAsync(CancellationToken.None);
        Console.WriteLine($"[Client:Plugin] StopAsync completed: {_plugin.PluginId}");

        if (_viewHandle is not null)
        {
            await DetachViewAsync(_viewHandle, CancellationToken.None);
            Console.WriteLine($"[Client:Plugin] DetachAsync completed: {_plugin.PluginId}");

            await _viewHandle.DisposeAsync();
            _viewHandle = null;
            Console.WriteLine($"[Client:Plugin] ViewHandle disposed: {_plugin.PluginId}");
        }

        await _plugin.DisposeAsync();
        Console.WriteLine($"[Client:Plugin] Plugin disposed: {_plugin.PluginId}");
        _plugin = null;

        _cts?.Dispose();
        _cts = null;

        _context?.Unload();
        _context = null;
        Console.WriteLine("[Client:Plugin] ALC unload requested.");
    }

    public async ValueTask DisposeAsync() => await UnloadAsync();

    /// <summary>
    /// 在 UI 线程上执行视图脱离，并真正等待插件返回的 detach Task 完成。
    /// <para>
    /// 不能直接 <c>await Dispatcher.InvokeAsync(async () =&gt; ...)</c>：
    /// 那只等到 DispatcherOperation 本身（即 async lambda 第一次让出），
    /// 内层 detach Task 尚未完成就会继续走 DisposeAsync，与视图脱离并发竞争，
    /// 且 detach 异常无人观察。必须对 <c>DispatcherOperation&lt;Task&gt;.Task</c> 做 Unwrap。
    /// </para>
    /// </summary>
    internal static async Task DetachViewAsync(IPluginViewHandle viewHandle, CancellationToken ct)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("无法在没有 WPF Application Dispatcher 的情况下分离插件视图。");

        if (dispatcher.CheckAccess())
        {
            await viewHandle.DetachAsync(ct);
            return;
        }

        var operation = dispatcher.InvokeAsync(() => viewHandle.DetachAsync(ct));
        await operation.Task.Unwrap();
    }

    private static IWhiteboardWpfPlugin FindPlugin(Assembly asm)
    {
        var pluginType = asm.GetExportedTypes()
            .FirstOrDefault(type => !type.IsAbstract && typeof(IWhiteboardWpfPlugin).IsAssignableFrom(type))
            ?? throw new InvalidOperationException(
                $"Assembly '{asm.GetName().Name}' does not export a type implementing IWhiteboardWpfPlugin.");

        return (IWhiteboardWpfPlugin)(Activator.CreateInstance(pluginType)
            ?? throw new InvalidOperationException($"Could not instantiate {pluginType.FullName}."));
    }

    /// <summary>
    /// 在调用方已经退出插件卸载方法、并给 WPF Dispatcher 一次空闲处理机会后，
    /// 再验证可回收 ALC 是否真正离开托管堆。
    /// <para>
    /// 不能在 <see cref="UnloadAsync"/> 的最后一行立刻做这项检查：
    /// JIT 与异步状态机可能仍暂时保留卸载过程中的局部对象，
    /// 而 WPF 对刚从视觉树移除的元素也可能还在处理 Unloaded/Layout 队列。
    /// </para>
    /// </summary>
    internal void VerifyAlcUnloaded()
    {
        if (_weakContext is null) return;

        for (var i = 0; i < 10; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            if (!_weakContext.TryGetTarget(out _))
            {
                Console.WriteLine("[Client:Plugin] ALC released successfully.");
                _weakContext = null;
                return;
            }
        }

        var warning = "[PluginManager] Warning: ALC WeakReference still alive after 10 GC rounds — possible leak.";
        System.Diagnostics.Debug.WriteLine(warning);
        Console.WriteLine(warning);
    }
}
