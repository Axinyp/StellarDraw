using System.Windows;
using System.Windows.Threading;

namespace Axinyp.Wpf.Hosting.Wpf.Tests;

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

internal sealed class WpfDispatcherTestHost : IAsyncDisposable
{
    private readonly TaskCompletionSource<Dispatcher> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;

    public WpfDispatcherTestHost()
    {
        _thread = new Thread(() =>
        {
            _ready.TrySetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public async Task<Dispatcher> GetDispatcherAsync() => await _ready.Task.ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        var dispatcher = await GetDispatcherAsync().ConfigureAwait(false);
        if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
            dispatcher.InvokeShutdown();
        _thread.Join(TimeSpan.FromSeconds(5));
    }
}
