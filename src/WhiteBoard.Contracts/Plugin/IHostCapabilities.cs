using System.Globalization;

namespace WhiteBoard.Contracts.Plugin;

/// <summary>
/// 宿主能力接口：插件通过此接口访问宿主提供的对话框、文件选择器、遥测、鉴权、日志、
/// 配置、快捷键等平台服务，而无需依赖具体宿主实现。
/// </summary>
public interface IHostCapabilities
{
    IHostDialogService Dialog { get; }
    IHostFilePicker Files { get; }
    IHostTelemetry Metrics { get; }
    IHostAuthContext Auth { get; }
    IHostLogger Logger { get; }
    IHostSettings Settings { get; }
    IHostShortcuts Shortcuts { get; }
    CultureInfo CurrentCulture { get; }
}

/// <summary>宿主对话框服务：确认框与错误提示框。</summary>
public interface IHostDialogService
{
    Task<bool> ConfirmAsync(string title, string message, CancellationToken ct = default);

    Task ShowErrorAsync(string title, string message, CancellationToken ct = default);
}

/// <summary>宿主文件选择器：打开与保存文件对话框。</summary>
public interface IHostFilePicker
{
    Task<string?> PickOpenFileAsync(string filter, CancellationToken ct = default);

    Task<string?> PickSaveFileAsync(string defaultName, string filter, CancellationToken ct = default);
}

/// <summary>宿主遥测：事件、指标、异常上报。</summary>
public interface IHostTelemetry
{
    void TrackEvent(string name, IReadOnlyDictionary<string, string>? properties = null);

    void TrackMetric(string name, double value);

    void TrackException(Exception ex, string? context = null);
}

/// <summary>宿主鉴权上下文：当前登录用户信息（UserId/UserName/IsGuest）。</summary>
public interface IHostAuthContext
{
    string? UserId { get; }
    string? UserName { get; }
    bool IsGuest { get; }
}

/// <summary>宿主日志器：分级日志（Debug/Info/Warning/Error），由宿主决定写入目标。</summary>
public interface IHostLogger
{
    void LogDebug(string message);

    void LogInfo(string message);

    void LogWarning(string message);

    void LogError(string message, Exception? ex = null);
}

/// <summary>
/// 宿主配置读取接口。键名约定使用小写 dot.notation，例如 "whiteboard.hub.url"。
/// </summary>
public interface IHostSettings
{
    string? GetString(string key);

    T? Get<T>(string key);

    /// <summary>主题强调色，#RRGGBB 格式（例如 "#0078D4"）。</summary>
    string AccentColorHex { get; }

    /// <summary>显示器物理 DPI（通常 96 或 192）。</summary>
    double DisplayDpi { get; }
}

/// <summary>宿主快捷键注册：插件可注册/注销全局快捷键。</summary>
public interface IHostShortcuts
{
    void Register(string shortcutId, string gesture, Action handler);

    void Unregister(string shortcutId);
}