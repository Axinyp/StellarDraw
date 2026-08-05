using System.Globalization;
using WhiteBoard.Contracts.Plugin;

namespace WhiteBoard.Client.Internal.PluginHosting;

/// <summary>
/// WhiteBoard.Client 首轮真实业务回接使用的临时宿主能力实现。
/// 这里的目标不是提供完整产品级宿主平台，而是让 `WhiteBoard.Plugin` 在正式 Client Shell 中
/// 以“离线模式”完成初始化、创建视图和基础白板交互。
/// </summary>
/// <remarks>
/// 之所以保留一个独立适配层，而不是直接把 Demo 的实现原样拷贝到窗口代码里，
/// 是为了明确告诉后续维护者：
/// 1. 这是一层过渡适配，不是最终长期宿主能力模型；
/// 2. 插件目前需要的对话框、日志、设置、文件选择器等能力是“够用即可”的临时版本；
/// 3. 后续若要接入真实设置、文件系统、凭据、快捷键或遥测，应在这里替换，而不是改插件。
/// </remarks>
internal sealed class ClientHostCapabilities : IHostCapabilities
{
    internal static readonly ClientHostCapabilities Instance = new();

    private ClientHostCapabilities()
    { }

    public IHostDialogService Dialog => ClientDialog.Instance;
    public IHostFilePicker Files => ClientFilePicker.Instance;
    public IHostTelemetry Metrics => ClientTelemetry.Instance;
    public IHostAuthContext Auth => ClientAuth.Instance;
    public IHostLogger Logger => ClientLogger.Instance;
    public IHostSettings Settings => ClientSettings.Instance;
    public IHostShortcuts Shortcuts => ClientShortcuts.Instance;
    public CultureInfo CurrentCulture => CultureInfo.CurrentUICulture;
}

internal sealed class ClientDialog : IHostDialogService
{
    internal static readonly ClientDialog Instance = new();

    public Task<bool> ConfirmAsync(string title, string message, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task ShowErrorAsync(string title, string message, CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class ClientFilePicker : IHostFilePicker
{
    internal static readonly ClientFilePicker Instance = new();

    public Task<string?> PickOpenFileAsync(string filter, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public Task<string?> PickSaveFileAsync(string defaultName, string filter, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}

internal sealed class ClientTelemetry : IHostTelemetry
{
    internal static readonly ClientTelemetry Instance = new();

    public void TrackEvent(string name, IReadOnlyDictionary<string, string>? properties = null)
    { }

    public void TrackMetric(string name, double value)
    { }

    public void TrackException(Exception ex, string? context = null)
        => System.Diagnostics.Debug.WriteLine($"[ClientTelemetry] {context}: {ex.Message}");
}

internal sealed class ClientAuth : IHostAuthContext
{
    internal static readonly ClientAuth Instance = new();
    public string? UserId => "local-user";
    public string? UserName => "Local User";
    public bool IsGuest => false;
}

internal sealed class ClientLogger : IHostLogger
{
    internal static readonly ClientLogger Instance = new();

    private static void Log(string level, string msg)
    {
        var line = $"[Client:{level}] {msg}";
        System.Diagnostics.Debug.WriteLine(line);
        Console.WriteLine(line);
    }

    public void LogDebug(string message) => Log("DBG", message);
    public void LogInfo(string message) => Log("INF", message);
    public void LogWarning(string message) => Log("WRN", message);
    public void LogError(string message, Exception? ex = null)
        => Log("ERR", ex is null ? message : $"{message} | {ex.Message}");
}

internal sealed class ClientSettings : IHostSettings
{
    internal static readonly ClientSettings Instance = new();

    /// <summary>
    /// 首轮故意不提供真实产品设置绑定。
    /// 这样可以强制当前白板插件以离线模式启动，避免在宿主回接阶段同时拉入协同、Token、远程配置等额外变量。
    /// </summary>
    public string? GetString(string key) => null;

    public T? Get<T>(string key) => default;

    public string AccentColorHex => "#0078D4";
    public double DisplayDpi => 96.0;
}

internal sealed class ClientShortcuts : IHostShortcuts
{
    internal static readonly ClientShortcuts Instance = new();

    public void Register(string shortcutId, string gesture, Action handler)
    { }

    public void Unregister(string shortcutId)
    { }
}
