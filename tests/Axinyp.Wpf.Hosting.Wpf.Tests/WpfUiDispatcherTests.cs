using System.Windows.Threading;
using Axinyp.Wpf.Hosting.Abstractions.Wpf;
using Axinyp.Wpf.Hosting.Wpf.Internal;

namespace Axinyp.Wpf.Hosting.Wpf.Tests;

public sealed class WpfUiDispatcherTests
{
    [Fact]
    public async Task InvokeAsync_ExecutesInlineAndMarshalsBackgroundCalls()
    {
        await Sta.RunAsync(async () =>
        {
            IUiDispatcher dispatcher = new WpfUiDispatcher();
            var applicationDispatcher = dispatcher.GetApplicationDispatcher();
            var inlineThreadId = Environment.CurrentManagedThreadId;

            var inlineResult = await dispatcher.InvokeAsync(applicationDispatcher, () => Environment.CurrentManagedThreadId);
            var backgroundResult = await Task.Run(
                () => dispatcher.InvokeAsync(applicationDispatcher, () => Environment.CurrentManagedThreadId));

            Assert.Equal(inlineThreadId, inlineResult);
            Assert.Equal(inlineThreadId, backgroundResult);
        });
    }

    [Fact]
    public async Task InvokeAsync_AfterDispatcherShutdown_RejectsNewWork()
    {
        await using var host = new WpfDispatcherTestHost();
        var target = await host.GetDispatcherAsync();
        IUiDispatcher dispatcher = new WpfUiDispatcher();

        target.InvokeShutdown();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.InvokeAsync(target, static () => { }));

        Assert.Contains("正在关闭", exception.Message);
    }
}
