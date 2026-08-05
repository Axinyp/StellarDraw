using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using WhiteBoard.Contracts.Plugin;
using WhiteBoard.Contracts.Wpf;
using WhiteBoard.Core.Domain;

namespace WhiteBoard.Host.Demo;

/// <summary>
/// 管理单个插件的完整生命周期：加载 → 初始化 → 创建视图 → 启动 → 停止 → 卸载。
/// </summary>
public sealed class PluginManager : IAsyncDisposable
{
    private WhiteboardPluginContext? _context;
    private WeakReference<AssemblyLoadContext>? _weakContext;
    private IWhiteboardWpfPlugin? _plugin;
    private IPluginViewHandle? _viewHandle;
    private CancellationTokenSource? _cts;

    public IPluginViewHandle? ViewHandle => _viewHandle;

    internal WeakReference<AssemblyLoadContext>? LastUnloadedContext => _weakContext;

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
        string pluginDllPath,
        IPluginMessageBus bus,
        IHostCapabilities host,
        CancellationToken ct = default)
    {
        if (_context is not null || _plugin is not null || _viewHandle is not null || _cts is not null)
            throw new InvalidOperationException("A plugin is already loaded. Call UnloadAsync first.");

        var context = new WhiteboardPluginContext(pluginDllPath);
        var weakContext = new WeakReference<AssemblyLoadContext>(context);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IWhiteboardWpfPlugin? plugin = null;
        IPluginViewHandle? viewHandle = null;

        try
        {
            var assembly = context.LoadFromAssemblyPath(pluginDllPath);
            plugin = FindPlugin(assembly);

            await plugin.InitializeAsync(bus, host, cts.Token);
            viewHandle = plugin.CreateView(); // UI thread
            await plugin.StartAsync(cts.Token);

            _context = context;
            _weakContext = weakContext;
            _cts = cts;
            _plugin = plugin;
            _viewHandle = viewHandle;
        }
        catch (Exception loadFailure)
        {
            _weakContext = weakContext;
            try
            {
                await ReleaseAsync(plugin, viewHandle, cts, context, CancellationToken.None);
            }
            catch (Exception cleanupFailure)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[PluginManager] Plugin load cleanup failed: {cleanupFailure.Message}");
            }
            finally
            {
                ScheduleAlcVerification(weakContext);
            }

            ExceptionDispatchInfo.Capture(loadFailure).Throw();
            throw;
        }
    }

    /// <summary>
    /// 安全卸载：停止 → UI线程分离视图 → 释放插件 → 请求 ALC 卸载。
    /// </summary>
    public async Task UnloadAsync()
    {
        var context = _context;
        var plugin = _plugin;
        var viewHandle = _viewHandle;
        var cts = _cts;

        _context = null;
        _plugin = null;
        _viewHandle = null;
        _cts = null;

        if (context is null && plugin is null && viewHandle is null && cts is null)
            return;

        await ReleaseAsync(plugin, viewHandle, cts, context, CancellationToken.None);

        if (_weakContext is not null)
            ScheduleAlcVerification(_weakContext);
    }

    public async ValueTask DisposeAsync() => await UnloadAsync();

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

    private static async Task ReleaseAsync(
        IWhiteboardWpfPlugin? plugin,
        IPluginViewHandle? viewHandle,
        CancellationTokenSource? cts,
        WhiteboardPluginContext? context,
        CancellationToken ct)
    {
        Exception? failure = null;

        try
        {
            cts?.Cancel();
        }
        catch (Exception ex)
        {
            CaptureFailure(ref failure, ex);
        }

        if (plugin is not null)
        {
            try
            {
                await plugin.StopAsync(ct);
            }
            catch (Exception ex)
            {
                CaptureFailure(ref failure, ex);
            }
        }

        if (viewHandle is not null)
        {
            try
            {
                await DetachViewAsync(viewHandle, ct);
            }
            catch (Exception ex)
            {
                CaptureFailure(ref failure, ex);
            }

            try
            {
                await viewHandle.DisposeAsync();
            }
            catch (Exception ex)
            {
                CaptureFailure(ref failure, ex);
            }
        }

        if (plugin is not null)
        {
            try
            {
                await plugin.DisposeAsync();
            }
            catch (Exception ex)
            {
                CaptureFailure(ref failure, ex);
            }
        }

        try
        {
            cts?.Dispose();
        }
        catch (Exception ex)
        {
            CaptureFailure(ref failure, ex);
        }

        try
        {
            context?.Unload();
        }
        catch (Exception ex)
        {
            CaptureFailure(ref failure, ex);
        }

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void CaptureFailure(ref Exception? failure, Exception exception)
        => failure ??= exception;

    private static IWhiteboardWpfPlugin FindPlugin(Assembly assembly)
    {
        var pluginType = assembly.GetExportedTypes()
            .FirstOrDefault(type => !type.IsAbstract && typeof(IWhiteboardWpfPlugin).IsAssignableFrom(type))
            ?? throw new InvalidOperationException(
                $"Assembly '{assembly.GetName().Name}' does not export a type implementing IWhiteboardWpfPlugin.");

        return (IWhiteboardWpfPlugin)(Activator.CreateInstance(pluginType)
            ?? throw new InvalidOperationException($"Could not instantiate {pluginType.FullName}."));
    }

    private static void ScheduleAlcVerification(WeakReference<AssemblyLoadContext> weakContext)
        => _ = Task.Run(() => VerifyAlcUnloaded(weakContext));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void VerifyAlcUnloaded(WeakReference<AssemblyLoadContext> weakContext)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

            if (!weakContext.TryGetTarget(out _))
                return;
        }

        System.Diagnostics.Debug.WriteLine(
            "[PluginManager] Warning: ALC WeakReference still alive after deferred GC verification — possible retained reference.");
    }
}
