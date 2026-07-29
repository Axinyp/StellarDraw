using System.Globalization;
using WhiteBoard.Contracts.Plugin;

namespace StellarDraw.Axinyp.Spike;

/// <summary>
/// legacy plugin 的离线、最小宿主能力。
/// 不提供 hub URL、token、远端身份或遥测出口，避免将协同与机密管理带入 Spike。
/// </summary>
internal sealed class LegacyHostCapabilities : IHostCapabilities
{
    /// <summary>
    /// 获取唯一的离线能力实例。
    /// </summary>
    public static LegacyHostCapabilities Offline { get; } = new();

    private LegacyHostCapabilities()
    {
    }

    /// <inheritdoc />
    public IHostDialogService Dialog { get; } = new NoOpDialogService();

    /// <inheritdoc />
    public IHostFilePicker Files { get; } = new NoOpFilePicker();

    /// <inheritdoc />
    public IHostTelemetry Metrics { get; } = new NoOpTelemetry();

    /// <inheritdoc />
    public IHostAuthContext Auth { get; } = new GuestAuthContext();

    /// <inheritdoc />
    public IHostLogger Logger { get; } = new DebugLogger();

    /// <inheritdoc />
    public IHostSettings Settings { get; } = new OfflineSettings();

    /// <inheritdoc />
    public IHostShortcuts Shortcuts { get; } = new NoOpShortcuts();

    /// <inheritdoc />
    public CultureInfo CurrentCulture => CultureInfo.CurrentUICulture;

    private sealed class NoOpDialogService : IHostDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task ShowErrorAsync(string title, string message, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoOpFilePicker : IHostFilePicker
    {
        public Task<string?> PickOpenFileAsync(string filter, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string defaultName, string filter, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoOpTelemetry : IHostTelemetry
    {
        public void TrackEvent(string name, IReadOnlyDictionary<string, string>? properties = null)
        {
        }

        public void TrackMetric(string name, double value)
        {
        }

        public void TrackException(Exception ex, string? context = null)
        {
        }
    }

    private sealed class GuestAuthContext : IHostAuthContext
    {
        public string? UserId => null;
        public string? UserName => null;
        public bool IsGuest => true;
    }

    private sealed class DebugLogger : IHostLogger
    {
        public void LogDebug(string message) => System.Diagnostics.Debug.WriteLine(message);
        public void LogInfo(string message) => System.Diagnostics.Debug.WriteLine(message);
        public void LogWarning(string message) => System.Diagnostics.Debug.WriteLine(message);
        public void LogError(string message, Exception? ex = null) =>
            System.Diagnostics.Debug.WriteLine(ex is null ? message : $"{message}: {ex}");
    }

    private sealed class OfflineSettings : IHostSettings
    {
        public string? GetString(string key) => null;
        public T? Get<T>(string key) => default;
        public string AccentColorHex => "#0078D4";
        public double DisplayDpi => 96d;
    }

    private sealed class NoOpShortcuts : IHostShortcuts
    {
        public void Register(string shortcutId, string gesture, Action handler)
        {
        }

        public void Unregister(string shortcutId)
        {
        }
    }
}
