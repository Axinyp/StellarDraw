using System.Reflection;
using System.Runtime.Loader;
using WhiteBoard.Contracts.Plugin;
using WhiteBoard.Contracts.Wpf;
using WhiteBoard.Core.Domain;

namespace WhiteBoard.Host.Demo;

/// <summary>
/// 管理单个插件的完整生命周期：加载 → 初始化 → 创建视图 → 启动 → 停止 → 卸载。
/// 卸载后通过 WeakReference 验证 ALC 被 GC 回收（防止泄漏）。
/// </summary>
public sealed class PluginManager : IAsyncDisposable
{
    private WhiteboardPluginContext? _context;
    private WeakReference<AssemblyLoadContext>? _weakContext;
    private IWhiteboardWpfPlugin?  _plugin;
    private IPluginViewHandle?     _viewHandle;
    private CancellationTokenSource? _cts;

    public IPluginViewHandle? ViewHandle => _viewHandle;

    /// <summary>
    /// 通过反射获取插件内部的 WhiteboardSession 实例。
    /// WhiteBoard.Core 在 SharedAssemblies 中共享，因此类型标识与宿主侧一致，as 转型安全。
    /// </summary>
    public WhiteboardSession? GetExportedSession()
    {
        if (_plugin is null) return null;
        var field = _plugin.GetType()
            .GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(_plugin) as WhiteboardSession;
    }

    /// <summary>
    /// 从指定路径加载插件 DLL，完成初始化和视图创建。
    /// 调用方负责在 UI 线程调用此方法（CreateView 需要）。
    /// </summary>
    public async Task LoadAsync(
        string              pluginDllPath,
        IPluginMessageBus   bus,
        IHostCapabilities   host,
        CancellationToken   ct = default)
    {
        if (_plugin is not null)
            throw new InvalidOperationException("A plugin is already loaded. Call UnloadAsync first.");

        _context     = new WhiteboardPluginContext(pluginDllPath);
        _weakContext = new WeakReference<AssemblyLoadContext>(_context);

        var asm    = _context.LoadFromAssemblyPath(pluginDllPath);
        var plugin = FindPlugin(asm);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await plugin.InitializeAsync(bus, host, _cts.Token);
        _viewHandle = plugin.CreateView(); // UI thread
        await plugin.StartAsync(_cts.Token);

        _plugin = plugin;
    }

    /// <summary>
    /// 安全卸载：停止 → UI线程分离视图 → 释放插件 → GC验证。
    /// </summary>
    public async Task UnloadAsync()
    {
        if (_plugin is null) return;

        _cts?.Cancel();

        await _plugin.StopAsync(CancellationToken.None);

        if (_viewHandle is not null)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                await _viewHandle.DetachAsync(CancellationToken.None));
            await _viewHandle.DisposeAsync();
            _viewHandle = null;
        }

        await _plugin.DisposeAsync();
        _plugin = null;

        _context?.Unload();
        _context = null;

        VerifyAlcUnloaded();
    }

    public async ValueTask DisposeAsync() => await UnloadAsync();

    private static IWhiteboardWpfPlugin FindPlugin(System.Reflection.Assembly asm)
    {
        var pluginType = asm.GetExportedTypes()
            .FirstOrDefault(t => !t.IsAbstract && typeof(IWhiteboardWpfPlugin).IsAssignableFrom(t))
            ?? throw new InvalidOperationException(
                $"Assembly '{asm.GetName().Name}' does not export a type implementing IWhiteboardWpfPlugin.");

        return (IWhiteboardWpfPlugin)(Activator.CreateInstance(pluginType)
            ?? throw new InvalidOperationException($"Could not instantiate {pluginType.FullName}."));
    }

    private void VerifyAlcUnloaded()
    {
        if (_weakContext is null) return;

        // 给 GC 两轮机会
        for (int i = 0; i < 10; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            if (!_weakContext.TryGetTarget(out _))
                return; // 已释放
        }

        System.Diagnostics.Debug.WriteLine("[PluginManager] Warning: ALC WeakReference still alive after 10 GC rounds — possible leak.");
    }
}
