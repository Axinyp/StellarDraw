using WhiteBoard.Client.Internal.PluginHosting;
using WhiteBoard.Contracts.Wpf;

namespace WhiteBoard.Client.Tests;

public sealed class PluginManagerLifecycleTests
{
    [Fact]
    public async Task DetachViewAsync_WaitsForDelayedDetachTask()
    {
        DelayedViewHandle? handle = null;
        await Sta.RunAsync(() =>
        {
            handle = new DelayedViewHandle(new Grid());
            return Task.CompletedTask;
        });

        var detaching = PluginManager.DetachViewAsync(handle!, CancellationToken.None);

        // 排空一轮 Dispatcher，确保 detach 委托已被处理；
        // 若实现只等待 DispatcherOperation 而未 Unwrap 内层 Task，此时 detaching 会提前完成。
        await Sta.RunAsync(() => Task.CompletedTask);
        Assert.False(detaching.IsCompleted);

        handle!.CompleteDetach();
        await detaching.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(handle.Detached);
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
