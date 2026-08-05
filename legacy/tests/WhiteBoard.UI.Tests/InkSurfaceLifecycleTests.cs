using System.Windows.Controls;
using System.Windows.Threading;
using WhiteBoard.Contracts.Services;
using WhiteBoard.Core.Domain;
using WhiteBoard.UI.Controls;
using WhiteBoard.UI.ViewModels;

namespace WhiteBoard.UI.Tests;

public sealed class InkSurfaceLifecycleTests
{
    [Fact]
    public async Task ClearingViewModel_DisposesSelectionControllerAndClearsOverlayCanvas()
    {
        await Sta.RunAsync(() =>
        {
            var session = new WhiteboardSession(NullCollaborationAdapter.Instance);
            var vm = new WhiteboardViewModel(session, Dispatcher.CurrentDispatcher);
            var surface = new InkSurface { ViewModel = vm };

            var overlayCanvas = GetOverlayCanvas(surface);
            Assert.Single(overlayCanvas.Children.OfType<SelectionToolbar>());

            surface.ViewModel = null;

            Assert.Empty(overlayCanvas.Children);

            vm.Dispose();
            return session.DisposeAsync().AsTask();
        });
    }

    private static Canvas GetOverlayCanvas(InkSurface surface)
    {
        var field = typeof(InkSurface).GetField("_overlayCanvas",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("InkSurface._overlayCanvas field was not found.");

        return Assert.IsType<Canvas>(field.GetValue(surface));
    }
}
