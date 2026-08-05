using System.Windows;
using System.Windows.Threading;

namespace WhiteBoard.Host.Demo.Tests;

internal static class StaDispatcher
{
    private static readonly Lazy<DispatcherHost> SharedHost = new(() => new DispatcherHost());

    public static Task RunAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return SharedHost.Value.RunAsync(action);
    }

    public static async Task DrainAsync()
    {
        var dispatcher = await SharedHost.Value.Dispatcher.ConfigureAwait(false);
        await dispatcher.InvokeAsync(
            static () => { },
            DispatcherPriority.ApplicationIdle);
    }

    private sealed class DispatcherHost
    {
        private readonly TaskCompletionSource<Dispatcher> _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Dispatcher> Dispatcher => _ready.Task;

        public DispatcherHost()
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
                IsBackground = true,
                Name = nameof(WhiteBoard) + "PluginTestSta"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        public async Task RunAsync(Func<Task> action)
        {
            var dispatcher = await Dispatcher.ConfigureAwait(false);
            await dispatcher.InvokeAsync(action).Task.Unwrap().ConfigureAwait(false);
        }
    }
}
