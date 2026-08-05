using System.Windows.Threading;

namespace WhiteBoard.Client.Tests;

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
        private readonly TaskCompletionSource<Dispatcher> _ready =
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
                Dispatcher.Run();
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
