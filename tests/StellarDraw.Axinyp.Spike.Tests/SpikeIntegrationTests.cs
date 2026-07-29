using System.Windows;
using System.Windows.Controls;
using Axinyp.Wpf.Hosting.Abstractions.Wpf;
using Axinyp.Wpf.Hosting.Wpf.Extensions;
using Microsoft.Extensions.DependencyInjection;
using WhiteBoard.Contracts.Plugin;

namespace StellarDraw.Axinyp.Spike.Tests;

public sealed class LegacyPluginOptionsTests
{
    [Fact]
    public void FromArguments_WithoutLegacyPluginOption_ReturnsNoAssemblyPath()
    {
        var options = LegacyPluginOptions.FromArguments(["--other", "value"]);

        Assert.Null(options.AssemblyPath);
    }

    [Fact]
    public void FromArguments_WithLegacyPluginOption_ReturnsSpecifiedPath()
    {
        const string pluginPath = "C:\\plugins\\WhiteBoard.Plugin.dll";

        var options = LegacyPluginOptions.FromArguments(["--legacy-plugin", pluginPath]);

        Assert.Equal(pluginPath, options.AssemblyPath);
    }

    [Theory]
    [MemberData(nameof(MissingOrBlankPluginArguments))]
    public void FromArguments_WithMissingOrBlankPath_ThrowsArgumentException(string[] arguments)
    {
        var exception = Assert.Throws<ArgumentException>(() => LegacyPluginOptions.FromArguments(arguments));

        Assert.Equal("--legacy-plugin", exception.ParamName);
    }

    public static IEnumerable<object[]> MissingOrBlankPluginArguments =>
    [
        [new[] { "--legacy-plugin" }],
        [new[] { "--legacy-plugin", " " }],
    ];
}

public sealed class LegacyPluginMessageBusTests
{
    [Fact]
    public async Task PublishAsync_BroadcastMessage_DeliversToEverySubscription()
    {
        var bus = new LegacyPluginMessageBus();
        await using var first = bus.Subscribe("first");
        await using var second = bus.Subscribe("second");
        var message = CreateMessage(targetPluginId: null, topic: "board.updated");

        await bus.PublishAsync(message);

        Assert.Equal(message, await ReadNextAsync(first));
        Assert.Equal(message, await ReadNextAsync(second));
    }

    [Fact]
    public async Task PublishAsync_TargetedMessage_DeliversOnlyToMatchingSubscription()
    {
        var bus = new LegacyPluginMessageBus();
        await using var target = bus.Subscribe("target");
        await using var other = bus.Subscribe("other");
        var message = CreateMessage(targetPluginId: "target", topic: "board.updated");

        await bus.PublishAsync(message);

        Assert.Equal(message, await ReadNextAsync(target));
        await AssertNoMessageAsync(other);
    }

    [Fact]
    public async Task PublishAsync_AppliesSubscriptionFilterAndStopsAfterDisposal()
    {
        var bus = new LegacyPluginMessageBus();
        var subscription = bus.Subscribe("consumer", message => message.Topic == "accepted");
        var rejected = CreateMessage(targetPluginId: null, topic: "rejected");
        var accepted = CreateMessage(targetPluginId: null, topic: "accepted");

        await bus.PublishAsync(rejected);
        await AssertNoMessageAsync(subscription);

        await bus.PublishAsync(accepted);
        Assert.Equal(accepted, await ReadNextAsync(subscription));

        await subscription.DisposeAsync();
        await bus.PublishAsync(accepted);
        await AssertNoMessageAsync(subscription);
    }

    [Fact]
    public async Task PublishAsync_WithCancelledToken_ThrowsBeforeDispatch()
    {
        var bus = new LegacyPluginMessageBus();
        await using var subscription = bus.Subscribe("consumer");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => bus.PublishAsync(CreateMessage(null, "board.updated"), cancellation.Token).AsTask());
        await AssertNoMessageAsync(subscription);
    }

    private static PluginMessage CreateMessage(string? targetPluginId, string topic) => new(
        "message-id",
        topic,
        "source",
        targetPluginId,
        1,
        "1.0",
        null,
        DateTimeOffset.UnixEpoch,
        ReadOnlyMemory<byte>.Empty);

    private static async Task<PluginMessage> ReadNextAsync(IPluginSubscription subscription)
    {
        await using var enumerator = subscription.Messages.GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        return enumerator.Current;
    }

    private static async Task AssertNoMessageAsync(IPluginSubscription subscription)
    {
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = subscription.Messages.GetAsyncEnumerator(cancellation.Token);
        var readTask = enumerator.MoveNextAsync().AsTask();
        var completed = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromMilliseconds(100)));

        if (ReferenceEquals(completed, readTask))
        {
            Assert.False(await readTask);
            return;
        }

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask);
    }
}

public sealed class LegacyWhiteboardPluginBridgeTests
{
    [Theory]
    [InlineData("relative-plugin.dll")]
    [InlineData("C:\\plugins\\not-a-dll.txt")]
    public async Task LoadAsync_WithInvalidAssemblyPath_RejectsInputBeforeLoading(string assemblyPath)
    {
        await Sta.RunAsync(async () =>
        {
            var bridge = new LegacyWhiteboardPluginBridge();
            var pluginHost = new ContentControl();

            await Assert.ThrowsAsync<ArgumentException>(() => bridge.LoadAsync(assemblyPath, pluginHost));
            Assert.False(bridge.IsLoaded);
            Assert.Null(pluginHost.Content);
        });
    }

    [Fact]
    public async Task LoadAsync_WithMissingAssemblyPath_ReportsFileNotFoundAndLeavesBridgeEmpty()
    {
        await Sta.RunAsync(async () =>
        {
            var bridge = new LegacyWhiteboardPluginBridge();
            var pluginHost = new ContentControl();
            var missingPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dll");

            await Assert.ThrowsAsync<FileNotFoundException>(() => bridge.LoadAsync(missingPath, pluginHost));
            Assert.False(bridge.IsLoaded);
            Assert.Null(pluginHost.Content);
        });
    }

    [Fact]
    [Trait("Category", "LegacyCompatibility")]
    public async Task LoadAndUnloadAsync_WithTrustedLegacyPlugin_AttachesAndDetachesView()
    {
        if (!IsLegacyCompatibilitySmokeEnabled())
            return;

        await Sta.RunAsync(async () =>
        {
            var bridge = new LegacyWhiteboardPluginBridge();
            var pluginHost = new ContentControl();

            try
            {
                await bridge.LoadAsync(GetTrustedPluginAssemblyPath(), pluginHost);

                Assert.True(bridge.IsLoaded);
                Assert.NotNull(pluginHost.Content);

                await bridge.UnloadAsync(pluginHost);

                Assert.False(bridge.IsLoaded);
                Assert.Null(pluginHost.Content);
            }
            finally
            {
                if (bridge.IsLoaded)
                    await bridge.UnloadAsync(pluginHost);

                await bridge.DisposeAsync();
            }
        });
    }

    private const string TrustedLegacyPluginPathVariable = "STELLARDRAW_TRUSTED_LEGACY_PLUGIN";

    private static bool IsLegacyCompatibilitySmokeEnabled() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TrustedLegacyPluginPathVariable));

    private static string GetTrustedPluginAssemblyPath()
    {
        var pluginPath = Environment.GetEnvironmentVariable(TrustedLegacyPluginPathVariable)
            ?? throw new InvalidOperationException(
                $"{TrustedLegacyPluginPathVariable} must provide the trusted legacy plugin DLL path.");

        if (!Path.IsPathFullyQualified(pluginPath))
            throw new InvalidOperationException(
                $"{TrustedLegacyPluginPathVariable} must provide an absolute plugin DLL path.");

        return pluginPath;
    }
}

public sealed class MainWindowActivationTests
{
    [Fact]
    public async Task WindowManager_ShowAsync_ActivatesPublicServiceProviderConstructor()
    {
        await Sta.RunAsync(async () =>
        {
            var services = new ServiceCollection();
            services.AddWpfPresentation();
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton(new LegacyPluginOptions((string?)null));
            services.AddSingleton<LegacyWhiteboardPluginBridge>();
            services.AddView<MainWindowViewModel, MainWindow>();
            services.AddView<StatusRegionViewModel, StatusRegionView>();
            await using var provider = services.BuildServiceProvider();
            var windowManager = provider.GetRequiredService<IWindowManager>();

            var window = await windowManager.ShowAsync<MainWindowViewModel>();

            Assert.IsType<MainWindow>(window);
            Assert.IsType<MainWindowViewModel>(window.DataContext);
            window.Close();
        });
    }
}

internal static class Sta
{
    private static readonly Lazy<StaDispatcher> SharedDispatcher = new(() => new StaDispatcher());

    public static Task RunAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return SharedDispatcher.Value.RunAsync(action);
    }

    private sealed class StaDispatcher
    {
        private readonly TaskCompletionSource<System.Windows.Threading.Dispatcher> _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StaDispatcher()
        {
            var thread = new Thread(() =>
            {
                var application = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
                _ready.TrySetResult(application.Dispatcher);
                System.Windows.Threading.Dispatcher.Run();
            })
            {
                IsBackground = true
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        public async Task RunAsync(Func<Task> action)
        {
            var dispatcher = await _ready.Task.ConfigureAwait(false);
            await dispatcher.InvokeAsync(action).Task.Unwrap().ConfigureAwait(false);
        }
    }
}
