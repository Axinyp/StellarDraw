using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Controls;
using WhiteBoard.Contracts.Plugin;
using WhiteBoard.Contracts.Wpf;
using WhiteBoard.Host.Demo;
using Xunit;

namespace WhiteBoard.Host.Demo.Tests;

public sealed class RuntimePluginLifecycleTests
{
    [Fact]
    public async Task LoadUnloadAndReload_DetachesVisualTreeAndClearsManagerState()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            var manager = new PluginManager();
            var host = new Border();

            try
            {
                await manager.LoadAsync(PluginDllPath, new InMemoryMessageBus(), TestHostCapabilities.Instance);
                var firstView = manager.ViewHandle!.View;
                host.Child = firstView;

                host.Child = null;
                await manager.UnloadAsync();

                Assert.Null(manager.ViewHandle);
                Assert.Null(firstView.DataContext);
                Assert.Empty(Assert.IsAssignableFrom<Panel>(firstView).Children);

                await manager.LoadAsync(PluginDllPath, new InMemoryMessageBus(), TestHostCapabilities.Instance);
                host.Child = manager.ViewHandle!.View;
                host.Child = null;
                await manager.UnloadAsync();

                Assert.Null(manager.ViewHandle);
            }
            finally
            {
                host.Child = null;
                await manager.DisposeAsync();
            }
        });
    }

    [Fact]
    public async Task Load_WhenInitializeFails_CleansPartialStateAndAllowsRetry()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            var manager = new PluginManager();
            var host = new Border();

            try
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    manager.LoadAsync(PluginDllPath, new ThrowingMessageBus(), TestHostCapabilities.Instance));

                Assert.Null(manager.ViewHandle);
                Assert.NotNull(manager.LastUnloadedContext);

                await manager.LoadAsync(PluginDllPath, new InMemoryMessageBus(), TestHostCapabilities.Instance);
                host.Child = manager.ViewHandle!.View;
                host.Child = null;
                await manager.UnloadAsync();

                Assert.Null(manager.ViewHandle);
            }
            finally
            {
                host.Child = null;
                await manager.DisposeAsync();
            }
        });
    }

    [Fact]
    public async Task DetachViewAsync_WaitsForDelayedDetachTask()
    {
        DelayedViewHandle? handle = null;
        await StaDispatcher.RunAsync(() =>
        {
            handle = new DelayedViewHandle(new Grid());
            return Task.CompletedTask;
        });

        var detaching = PluginManager.DetachViewAsync(handle!, CancellationToken.None);
        Assert.False(detaching.IsCompleted);

        handle!.CompleteDetach();
        await detaching;

        Assert.True(handle.Detached);
    }

    [Fact]
    public async Task Unload_ReleasesCollectibleContextAfterCallerReferencesLeaveScope()
    {
        var weakContext = await LoadAndUnloadAsync();
        await StaDispatcher.DrainAsync();

        for (var attempt = 0; attempt < 20 && IsAlive(weakContext); attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            await StaDispatcher.DrainAsync();
        }

        Assert.False(IsAlive(weakContext));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference<AssemblyLoadContext>> LoadAndUnloadAsync()
    {
        WeakReference<AssemblyLoadContext>? weakContext = null;

        await StaDispatcher.RunAsync(async () =>
        {
            var manager = new PluginManager();
            var host = new Border();

            try
            {
                await manager.LoadAsync(PluginDllPath, new InMemoryMessageBus(), TestHostCapabilities.Instance);
                host.Child = manager.ViewHandle!.View;
                host.Child = null;
                await manager.UnloadAsync();
                weakContext = manager.LastUnloadedContext;
            }
            finally
            {
                host.Child = null;
                await manager.DisposeAsync();
            }
        });

        return weakContext ?? throw new InvalidOperationException("插件卸载后未保留 ALC 弱引用。");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsAlive(WeakReference<AssemblyLoadContext> weakContext)
        => weakContext.TryGetTarget(out _);

    private static string PluginDllPath => Path.Combine(
        AppContext.BaseDirectory,
        "plugins",
        "whiteboard",
        "WhiteBoard.Plugin.dll");

    private sealed class TestHostCapabilities : IHostCapabilities
    {
        public static readonly TestHostCapabilities Instance = new();

        public IHostDialogService Dialog => MockHostCapabilities.Instance.Dialog;
        public IHostFilePicker Files => MockHostCapabilities.Instance.Files;
        public IHostTelemetry Metrics => MockHostCapabilities.Instance.Metrics;
        public IHostAuthContext Auth => MockHostCapabilities.Instance.Auth;
        public IHostLogger Logger => MockHostCapabilities.Instance.Logger;
        public IHostSettings Settings { get; } = new TestHostSettings();
        public IHostShortcuts Shortcuts => MockHostCapabilities.Instance.Shortcuts;
        public System.Globalization.CultureInfo CurrentCulture => MockHostCapabilities.Instance.CurrentCulture;
    }

    private sealed class TestHostSettings : IHostSettings
    {
        public string? GetString(string key) => key switch
        {
            "whiteboard.client.view-mode" => "full",
            _ => null,
        };

        public T? Get<T>(string key) => default;
        public string AccentColorHex => "#0078D4";
        public double DisplayDpi => 96.0;
    }

    private sealed class ThrowingMessageBus : IPluginMessageBus
    {
        public ValueTask PublishAsync(PluginMessage message, CancellationToken ct = default) => default;

        public IPluginSubscription Subscribe(string pluginId, Func<PluginMessage, bool>? filter = null)
            => throw new InvalidOperationException("测试注入的初始化失败。");
    }

    private sealed class DelayedViewHandle(FrameworkElement view) : IPluginViewHandle
    {
        private readonly TaskCompletionSource _detachCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string PluginId => "test.delayed";
        public FrameworkElement View { get; } = view;
        public bool Detached { get; private set; }

        public async Task DetachAsync(CancellationToken ct)
        {
            await _detachCompletion.Task.WaitAsync(ct);
            Detached = true;
        }

        public ValueTask DisposeAsync() => default;

        public void CompleteDetach() => _detachCompletion.SetResult();
    }
}
