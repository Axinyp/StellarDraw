using System.Windows.Threading;
using WhiteBoard.Contracts.Services;
using WhiteBoard.Core.Domain;
using WhiteBoard.UI.Controls;
using WhiteBoard.UI.ViewModels;

namespace WhiteBoard.UI.Tests;

public sealed class PageStripLifecycleTests
{
    [Fact]
    public async Task Dispose_UnhooksCurrentViewModelEventsAndPageButtons()
    {
        await Sta.RunAsync(() =>
        {
            var session = new WhiteboardSession(NullCollaborationAdapter.Instance);
            var vm = new WhiteboardViewModel(session, Dispatcher.CurrentDispatcher);
            var strip = new PageStrip { ViewModel = vm };

            strip.Dispose();

            vm.Pages.AddPage("after-dispose");

            var pageButtons = GetPageButtons(strip).ToList();
            Assert.Single(pageButtons);

            vm.Dispose();
            return session.DisposeAsync().AsTask();
        });
    }

    private static IEnumerable<Button> GetPageButtons(PageStrip strip)
    {
        var field = typeof(PageStrip).GetField("_pagePanel",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PageStrip._pagePanel field was not found.");

        var panel = Assert.IsType<StackPanel>(field.GetValue(strip));
        return panel.Children.OfType<Button>();
    }
}
