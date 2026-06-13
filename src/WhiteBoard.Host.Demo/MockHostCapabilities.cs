using System.Globalization;
using WhiteBoard.Contracts.Plugin;

namespace WhiteBoard.Host.Demo;

/// <summary>Demo 宿主能力的空实现，够插件调用不崩溃即可。</summary>
public sealed class MockHostCapabilities : IHostCapabilities
{
    public static readonly MockHostCapabilities Instance = new();
    private MockHostCapabilities() { }

    public IHostDialogService Dialog => MockDialog.Instance;
    public IHostFilePicker Files => MockFilePicker.Instance;
    public IHostTelemetry Metrics => MockTelemetry.Instance;
    public IHostAuthContext Auth => MockAuth.Instance;
    public IHostLogger Logger => ConsoleLogger.Instance;
    public IHostSettings Settings => MockSettings.Instance;
    public IHostShortcuts Shortcuts => MockShortcuts.Instance;
    public CultureInfo CurrentCulture => CultureInfo.CurrentUICulture;
}

file sealed class MockDialog : IHostDialogService
{
    internal static readonly MockDialog Instance = new();
    public Task<bool> ConfirmAsync(string title, string message, CancellationToken ct = default)
        => Task.FromResult(true);
    public Task ShowErrorAsync(string title, string message, CancellationToken ct = default)
        => Task.CompletedTask;
}

file sealed class MockFilePicker : IHostFilePicker
{
    internal static readonly MockFilePicker Instance = new();
    public Task<string?> PickOpenFileAsync(string filter, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
    public Task<string?> PickSaveFileAsync(string defaultName, string filter, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}

file sealed class MockTelemetry : IHostTelemetry
{
    internal static readonly MockTelemetry Instance = new();
    public void TrackEvent(string name, IReadOnlyDictionary<string, string>? properties = null) { }
    public void TrackMetric(string name, double value) { }
    public void TrackException(Exception ex, string? context = null)
        => System.Diagnostics.Debug.WriteLine($"[Telemetry] {context}: {ex.Message}");
}

file sealed class MockAuth : IHostAuthContext
{
    internal static readonly MockAuth Instance = new();
    public string? UserId => "demo-user";
    public string? UserName => "Demo User";
    public bool IsGuest => false;
}

file sealed class ConsoleLogger : IHostLogger
{
    internal static readonly ConsoleLogger Instance = new();
    private static void Log(string level, string msg)
        => System.Diagnostics.Debug.WriteLine($"[{level}] {msg}");
    public void LogDebug(string message) => Log("DBG", message);
    public void LogInfo(string message) => Log("INF", message);
    public void LogWarning(string message) => Log("WRN", message);
    public void LogError(string message, Exception? ex)
        => Log("ERR", ex is null ? message : $"{message} | {ex.Message}");
}

file sealed class MockSettings : IHostSettings
{
    internal static readonly MockSettings Instance = new();

    public string? GetString(string key) => key switch
    {
        "whiteboard.hub.url" => Environment.GetEnvironmentVariable("WHITEBOARD_HUB_URL"),
        "whiteboard.hub.token" => Environment.GetEnvironmentVariable("WHITEBOARD_HUB_TOKEN"),
        _ => null,
    };

    public T? Get<T>(string key) => default;
    public string AccentColorHex => "#0078D4";
    public double DisplayDpi => 96.0;
}

file sealed class MockShortcuts : IHostShortcuts
{
    internal static readonly MockShortcuts Instance = new();
    public void Register(string shortcutId, string gesture, Action handler) { }
    public void Unregister(string shortcutId) { }
}
