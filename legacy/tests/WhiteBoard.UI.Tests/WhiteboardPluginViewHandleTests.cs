using System.Windows.Threading;
using WhiteBoard.Contracts.Services;
using WhiteBoard.Core.Domain;
using WhiteBoard.Plugin;
using WhiteBoard.UI.Controls;
using WhiteBoard.UI.ViewModels;

namespace WhiteBoard.UI.Tests;

public sealed class WhiteboardPluginViewHandleTests
{
    [Fact]
    public async Task DetachAsync_ClearsExplicitViewReferencesAndBreaksVisualTree()
    {
        await Sta.RunAsync(async () =>
        {
            await using var session = new WhiteboardSession(NullCollaborationAdapter.Instance);
            var vm = new WhiteboardViewModel(session, Dispatcher.CurrentDispatcher);

            var toolbar = new InkToolbar { ViewModel = vm.Toolbar, Board = vm };
            var inkSurface = new InkSurface { ViewModel = vm };
            var pageStrip = new PageStrip { ViewModel = vm };
            var root = new Grid();
            root.Children.Add(toolbar);
            root.Children.Add(inkSurface);
            root.Children.Add(pageStrip);

            var handle = new WhiteboardPluginViewHandle("com.whiteboard.plugin", root);

            await handle.DetachAsync(CancellationToken.None);

            Assert.Null(toolbar.ViewModel);
            Assert.Null(toolbar.Board);
            Assert.Null(inkSurface.ViewModel);
            Assert.Null(pageStrip.ViewModel);
            Assert.Empty(root.Children);
            Assert.Throws<ObjectDisposedException>(() => _ = handle.View);

            vm.Dispose();
            await handle.DisposeAsync();
        });
    }
}
