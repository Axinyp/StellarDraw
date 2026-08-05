using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using WhiteBoard.Contracts.Plugin;
using WhiteBoard.Contracts.Wpf;

namespace StellarDraw.Axinyp.Spike;

/// <summary>
/// 消费者私有的 legacy 白板插件兼容桥接器。
/// 它只承接旧公开 contracts 的生命周期，既不是 Axinyp <c>IModule</c>，也不构成代码安全隔离。
/// </summary>
internal sealed class LegacyWhiteboardPluginBridge : IAsyncDisposable
{
    private LegacyWhiteboardPluginLoadContext? _loadContext;
    private IWhiteboardWpfPlugin? _plugin;
    private IPluginViewHandle? _viewHandle;
    private CancellationTokenSource? _lifecycleCancellation;

    /// <summary>
    /// 是否已完成 legacy plugin 的初始化、视图创建与启动。
    /// </summary>
    public bool IsLoaded => _plugin is not null;

    /// <summary>
    /// 加载 legacy plugin 并将其视图附加到消费者专用内容槽。
    /// </summary>
    /// <param name="pluginAssemblyPath">legacy plugin 主程序集的绝对 DLL 路径。</param>
    /// <param name="pluginHost">专用于 legacy View 的内容承载控件，不能是 Axinyp Region。</param>
    /// <param name="cancellationToken">加载取消令牌。</param>
    public async Task LoadAsync(
        string pluginAssemblyPath,
        ContentControl pluginHost,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pluginHost);
        EnsureOnOwningDispatcher(pluginHost.Dispatcher);

        if (IsLoaded)
            throw new InvalidOperationException("Legacy 白板插件已加载；请先卸载当前插件。");

        var fullPath = ValidatePluginAssemblyPath(pluginAssemblyPath);
        var loadContext = new LegacyWhiteboardPluginLoadContext(fullPath);
        IWhiteboardWpfPlugin? plugin = null;
        IPluginViewHandle? viewHandle = null;
        CancellationTokenSource? lifecycleCancellation = null;

        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(fullPath);
            plugin = CreatePlugin(assembly);
            lifecycleCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            await plugin.InitializeAsync(new LegacyPluginMessageBus(), LegacyHostCapabilities.Offline,
                lifecycleCancellation.Token);
            viewHandle = plugin.CreateView();
            await plugin.StartAsync(lifecycleCancellation.Token);

            pluginHost.Content = viewHandle.View;

            _loadContext = loadContext;
            _plugin = plugin;
            _viewHandle = viewHandle;
            _lifecycleCancellation = lifecycleCancellation;
        }
        catch (Exception loadException)
        {
            var cleanupErrors = await CleanupFailedLoadAsync(
                pluginHost,
                plugin,
                viewHandle,
                lifecycleCancellation,
                loadContext);
            throw CreateLifecycleException("Legacy 白板插件加载失败。", loadException, cleanupErrors);
        }
    }

    /// <summary>
    /// 依次脱离 Shell 视觉树、等待 UI 空闲、停止 plugin、分离和释放 View，再释放 plugin 和请求 ALC 卸载。
    /// 所有释放步骤都会执行，以免前一步失败阻断后续资源回收。
    /// </summary>
    /// <param name="pluginHost">承载 legacy View 的消费者专用内容槽。</param>
    public async Task UnloadAsync(ContentControl pluginHost)
    {
        ArgumentNullException.ThrowIfNull(pluginHost);
        EnsureOnOwningDispatcher(pluginHost.Dispatcher);

        var errors = new List<Exception>();
        var plugin = _plugin;
        var viewHandle = _viewHandle;
        var lifecycleCancellation = _lifecycleCancellation;
        var loadContext = _loadContext;

        // 首先断开 Shell 对 legacy 视觉树的引用，再触发 plugin 的业务停止与资源释放。
        pluginHost.Content = null;
        pluginHost.UpdateLayout();
        await AwaitDispatcherIdleAsync(pluginHost.Dispatcher, errors);

        TryCancel(lifecycleCancellation, errors);
        if (plugin is not null)
            await TryAsync(() => plugin.StopAsync(CancellationToken.None), errors);
        if (viewHandle is not null)
            await TryAsync(() => viewHandle.DetachAsync(CancellationToken.None), errors);
        if (viewHandle is not null)
            await TryAsync(viewHandle.DisposeAsync, errors);
        if (plugin is not null)
            await TryAsync(plugin.DisposeAsync, errors);

        lifecycleCancellation?.Dispose();
        loadContext?.Unload();
        ClearState();

        if (errors.Count > 0)
            throw new AggregateException("Legacy 白板插件卸载出现一个或多个错误。", errors);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() =>
        _plugin is null
            ? ValueTask.CompletedTask
            : ValueTask.FromException(new InvalidOperationException(
                "释放 LegacyWhiteboardPluginBridge 前必须先从 Shell 调用 UnloadAsync。"));

    private async Task<List<Exception>> CleanupFailedLoadAsync(
        ContentControl pluginHost,
        IWhiteboardWpfPlugin? plugin,
        IPluginViewHandle? viewHandle,
        CancellationTokenSource? lifecycleCancellation,
        LegacyWhiteboardPluginLoadContext loadContext)
    {
        var errors = new List<Exception>();
        pluginHost.Content = null;
        pluginHost.UpdateLayout();
        await AwaitDispatcherIdleAsync(pluginHost.Dispatcher, errors);

        TryCancel(lifecycleCancellation, errors);
        if (plugin is not null)
            await TryAsync(() => plugin.StopAsync(CancellationToken.None), errors);
        if (viewHandle is not null)
            await TryAsync(() => viewHandle.DetachAsync(CancellationToken.None), errors);
        if (viewHandle is not null)
            await TryAsync(viewHandle.DisposeAsync, errors);
        if (plugin is not null)
            await TryAsync(plugin.DisposeAsync, errors);

        lifecycleCancellation?.Dispose();
        loadContext.Unload();
        return errors;
    }

    private void ClearState()
    {
        _plugin = null;
        _viewHandle = null;
        _lifecycleCancellation = null;
        _loadContext = null;
    }

    private static string ValidatePluginAssemblyPath(string pluginAssemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginAssemblyPath);
        if (!Path.IsPathFullyQualified(pluginAssemblyPath))
            throw new ArgumentException("legacy plugin 路径必须是绝对路径。", nameof(pluginAssemblyPath));

        var fullPath = Path.GetFullPath(pluginAssemblyPath);
        if (!string.Equals(Path.GetExtension(fullPath), ".dll", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("legacy plugin 路径必须指向 .dll 文件。", nameof(pluginAssemblyPath));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("未找到 legacy plugin DLL。", fullPath);

        _ = AssemblyName.GetAssemblyName(fullPath);
        return fullPath;
    }

    private static IWhiteboardWpfPlugin CreatePlugin(Assembly assembly)
    {
        var pluginType = assembly.GetExportedTypes()
            .FirstOrDefault(type => !type.IsAbstract && typeof(IWhiteboardWpfPlugin).IsAssignableFrom(type))
            ?? throw new InvalidOperationException(
                $"程序集 '{assembly.GetName().Name}' 未导出 IWhiteboardWpfPlugin 实现。");

        return (IWhiteboardWpfPlugin)(Activator.CreateInstance(pluginType)
            ?? throw new InvalidOperationException($"无法创建 legacy plugin '{pluginType.FullName}'。"));
    }

    private static async Task AwaitDispatcherIdleAsync(Dispatcher dispatcher, ICollection<Exception> errors)
    {
        try
        {
            if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                await dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle).Task;
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
    }

    private static void EnsureOnOwningDispatcher(Dispatcher dispatcher)
    {
        if (!dispatcher.CheckAccess())
            throw new InvalidOperationException("Legacy plugin 视图生命周期必须在其所属 WPF Dispatcher 上执行。");
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            throw new InvalidOperationException("WPF Dispatcher 正在关闭，不能操作 legacy plugin 视图。");
    }

    private static void TryCancel(CancellationTokenSource? cancellation, ICollection<Exception> errors)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
    }

    private static async Task TryAsync(Func<Task> operation, ICollection<Exception> errors)
    {
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
    }

    private static async Task TryAsync(Func<ValueTask> operation, ICollection<Exception> errors)
    {
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
    }

    private static Exception CreateLifecycleException(string message, Exception cause, IReadOnlyCollection<Exception> cleanupErrors)
    {
        if (cleanupErrors.Count == 0)
            return new InvalidOperationException(message, cause);

        return new AggregateException(message, [cause, .. cleanupErrors]);
    }
}
