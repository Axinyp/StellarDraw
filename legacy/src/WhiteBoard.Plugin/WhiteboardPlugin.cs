using System.Windows;
using System.Windows.Controls;
using WhiteBoard.Contracts.Plugin;
using WhiteBoard.Contracts.Services;
using WhiteBoard.Contracts.Wpf;
using WhiteBoard.Core.Domain;
using WhiteBoard.Plugin.Collaboration;
using WhiteBoard.Core.InkForecast;
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
    private IPluginMessageBus? _bus;
    private IHostCapabilities? _host;
    private WhiteboardSession? _session;
    private WhiteboardViewModel? _viewModel;
    private CancellationTokenSource? _cts;
    private IPluginSubscription? _subscription;
    private IAsyncDisposable? _collabDisposable;
    private Task? _messagePumpTask;

    public string PluginId => "com.whiteboard.plugin";
    public string Version => "0.5.0-phase5";

    public async Task InitializeAsync(
        IPluginMessageBus bus,
        IHostCapabilities host,
        CancellationToken ct)
    {
        _bus = bus;
        _host = host;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _subscription = bus.Subscribe(PluginId);
        _messagePumpTask = ProcessMessagesAsync(_subscription, _cts.Token);

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

        var token = host.Settings.GetString("whiteboard.hub.token");
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
        var session = _session ?? throw new InvalidOperationException("InitializeAsync must complete first");
        var dispatcher = Application.Current.Dispatcher;

        _viewModel = new WhiteboardViewModel(session, dispatcher);
        var viewMode = ResolveViewMode();
        _host?.Logger.LogInfo($"[{PluginId}] View mode: {viewMode}");
        var view = BuildView(_viewModel, viewMode);
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

        if (_messagePumpTask is not null)
        {
            try
            {
                await _messagePumpTask;
            }
            catch (OperationCanceledException)
            {
                // 这是主动停止插件时的正常路径：消息泵会随着 linked token 取消而退出。
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _viewModel?.Dispose();
        _viewModel = null;

        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
            _subscription = null;
        }

        if (_session is not null)
        {
            await _session.DisposeAsync();
            _session = null;
        }

        if (_collabDisposable is not null)
        {
            await _collabDisposable.DisposeAsync();
            _collabDisposable = null;
        }

        _cts?.Dispose();
        _cts = null;
        _messagePumpTask = null;
        _bus = null;
        _host = null;
    }

    // ── View construction ─────────────────────────────────────────────────────

    private static FrameworkElement BuildView(WhiteboardViewModel vm, string viewMode)
    {
        var mode = string.IsNullOrWhiteSpace(viewMode)
            ? "full"
            : viewMode.Trim().ToLowerInvariant();

        return mode switch
        {
            "empty" => new Grid(),
            "toolbar" => BuildToolbarOnly(vm),
            "pagestrip" => BuildPageStripOnly(vm),
            "inksurface" => BuildInkSurfaceOnly(vm),
            "toolbar+pagestrip" => BuildToolbarAndPageStrip(vm),
            "toolbar+inksurface" => BuildToolbarAndInkSurface(vm),
            "pagestrip+inksurface" => BuildPageStripAndInkSurface(vm),
            _ => BuildFullView(vm),
        };
    }

    private string ResolveViewMode()
        => _host?.Settings.GetString("whiteboard.client.view-mode")
           ?? Environment.GetEnvironmentVariable("WHITEBOARD_PLUGIN_VIEW_MODE")
           ?? "full";

    private static FrameworkElement BuildFullView(WhiteboardViewModel vm)
    {
        var toolbar = new InkToolbar { ViewModel = vm.Toolbar, Board = vm };
        var inkSurface = new InkSurface { ViewModel = vm };
        var pageStrip = new PageStrip { ViewModel = vm };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(toolbar, 0);
        Grid.SetRow(inkSurface, 1);
        Grid.SetRow(pageStrip, 2);

        grid.Children.Add(toolbar);
        grid.Children.Add(inkSurface);
        grid.Children.Add(pageStrip);

        return grid;
    }

    private static FrameworkElement BuildToolbarOnly(WhiteboardViewModel vm)
        => new InkToolbar { ViewModel = vm.Toolbar, Board = vm };

    private static FrameworkElement BuildPageStripOnly(WhiteboardViewModel vm)
        => new PageStrip { ViewModel = vm };

    private static FrameworkElement BuildInkSurfaceOnly(WhiteboardViewModel vm)
        => new InkSurface { ViewModel = vm };

    private static FrameworkElement BuildToolbarAndPageStrip(WhiteboardViewModel vm)
    {
        var panel = new DockPanel();
        var toolbar = new InkToolbar { ViewModel = vm.Toolbar, Board = vm };
        var pageStrip = new PageStrip { ViewModel = vm };
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(pageStrip, Dock.Bottom);
        panel.Children.Add(toolbar);
        panel.Children.Add(pageStrip);
        return panel;
    }

    private static FrameworkElement BuildToolbarAndInkSurface(WhiteboardViewModel vm)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new InkToolbar { ViewModel = vm.Toolbar, Board = vm };
        var inkSurface = new InkSurface { ViewModel = vm };
        Grid.SetRow(toolbar, 0);
        Grid.SetRow(inkSurface, 1);
        grid.Children.Add(toolbar);
        grid.Children.Add(inkSurface);
        return grid;
    }

    private static FrameworkElement BuildPageStripAndInkSurface(WhiteboardViewModel vm)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var inkSurface = new InkSurface { ViewModel = vm };
        var pageStrip = new PageStrip { ViewModel = vm };
        Grid.SetRow(inkSurface, 0);
        Grid.SetRow(pageStrip, 1);
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