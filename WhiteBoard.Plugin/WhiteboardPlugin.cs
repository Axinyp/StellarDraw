using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WhiteBoard.Contracts.Plugin;
using WhiteBoard.Contracts.Services;
using WhiteBoard.Contracts.Wpf;
using WhiteBoard.Core.Domain;
using WhiteBoard.Plugin.Collaboration;
using WhiteBoard.Plugin.InkForecast;
using WhiteBoard.UI.Controls;
using WhiteBoard.UI.ViewModels;

namespace WhiteBoard.Plugin;

/// <summary>
/// 白板插件主入口（Phase 5）。
/// 加载完整 UI：InkToolbar + InkSurface + PageStrip；
/// 可选接入 SignalR 协同（通过宿主设置键 "whiteboard.hub.url" 启用）。
/// </summary>
public sealed class WhiteboardPlugin : IWhiteboardWpfPlugin
{
    private IPluginMessageBus?         _bus;
    private IHostCapabilities?         _host;
    private WhiteboardSession?         _session;
    private WhiteboardViewModel?       _viewModel;
    private CancellationTokenSource?   _cts;
    private IPluginSubscription?       _subscription;
    private IAsyncDisposable?          _collabDisposable;

    public string PluginId => "com.whiteboard.plugin";
    public string Version  => "0.5.0-phase5";

    public async Task InitializeAsync(
        IPluginMessageBus bus,
        IHostCapabilities host,
        CancellationToken ct)
    {
        _bus  = bus;
        _host = host;
        _cts  = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _subscription = bus.Subscribe(PluginId);
        _ = ProcessMessagesAsync(_subscription, _cts.Token);

        var collab = await TryCreateCollabAdapterAsync(host, _cts.Token);

        _session = new WhiteboardSession(collab);
        _session.RegisterPredictor(new KalmanPredictor());
        await _session.StartAsync(_cts.Token);

        host.Logger.LogInfo($"[{PluginId}] Initialized (collab={collab.IsActive})");
    }

    // ── 协同适配器工厂 ────────────────────────────────────────────────────────

    private async Task<ICollaborationAdapter> TryCreateCollabAdapterAsync(
        IHostCapabilities host, CancellationToken ct)
    {
        var hubUrl = host.Settings.GetString("whiteboard.hub.url");
        if (string.IsNullOrWhiteSpace(hubUrl))
            return NullCollaborationAdapter.Instance;

        var token   = host.Settings.GetString("whiteboard.hub.token");
        var adapter = new SignalRCollaborationAdapter(new SignalRAdapterOptions(hubUrl, token));

        try
        {
            await adapter.ConnectAsync(ct);
            _collabDisposable = adapter;
            host.Logger.LogInfo($"[{PluginId}] Connected to hub: {hubUrl}");
            return adapter;
        }
        catch (Exception ex)
        {
            host.Logger.LogWarning($"[{PluginId}] Hub connect failed ({ex.Message}); running offline.");
            await adapter.DisposeAsync();
            return NullCollaborationAdapter.Instance;
        }
    }

    public IPluginViewHandle CreateView()
    {
        // CreateView is called on the UI thread by PluginManager
        var session    = _session ?? throw new InvalidOperationException("InitializeAsync must complete first");
        var dispatcher = Application.Current.Dispatcher;

        _viewModel = new WhiteboardViewModel(session, dispatcher);
        var view   = BuildView(_viewModel);
        return new WhiteboardPluginViewHandle(PluginId, view);
    }

    public Task StartAsync(CancellationToken ct)
    {
        _host?.Logger.LogInfo($"[{PluginId}] Started");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _host?.Logger.LogInfo($"[{PluginId}] Stopping");
        _cts?.Cancel();
        if (_session is not null)
            await _session.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _viewModel?.Dispose();
        if (_subscription is not null)
            await _subscription.DisposeAsync();
        if (_session is not null)
            await _session.DisposeAsync();
        if (_collabDisposable is not null)
            await _collabDisposable.DisposeAsync();
        _cts?.Dispose();
    }

    // ── View construction ─────────────────────────────────────────────────────

    private static FrameworkElement BuildView(WhiteboardViewModel vm)
    {
        var toolbar    = new InkToolbar { ViewModel = vm.Toolbar };
        var inkSurface = new InkSurface  { ViewModel = vm };
        var pageStrip  = new PageStrip   { ViewModel = vm };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(toolbar,    0);
        Grid.SetRow(inkSurface, 1);
        Grid.SetRow(pageStrip,  2);

        grid.Children.Add(toolbar);
        grid.Children.Add(inkSurface);
        grid.Children.Add(pageStrip);

        return grid;
    }

    // ── Background message pump ───────────────────────────────────────────────

    private static async Task ProcessMessagesAsync(IPluginSubscription sub, CancellationToken ct)
    {
        try
        {
            await foreach (var msg in sub.Messages.WithCancellation(ct))
                System.Diagnostics.Debug.WriteLine($"[Plugin] Received: {msg.Topic}");
        }
        catch (OperationCanceledException) { }
    }
}
