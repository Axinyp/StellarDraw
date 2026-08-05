using System.Windows.Threading;
using WhiteBoard.Contracts.Services;
using WhiteBoard.Core.Domain;
using WhiteBoard.UI.Controls;
using WhiteBoard.UI.ViewModels;

namespace WhiteBoard.UI.Tests;

public sealed class InkToolbarLifecycleTests
{
    [Fact]
    public async Task Dispose_UnhooksToolbarEvents()
    {
        await Sta.RunAsync(() =>
        {
            var session = new WhiteboardSession(NullCollaborationAdapter.Instance);
            var vm = new WhiteboardViewModel(session, Dispatcher.CurrentDispatcher);
            var toolbar = new InkToolbar { ViewModel = vm.Toolbar, Board = vm };

            toolbar.Dispose();

            var colorButtons = GetPrivateList<Button>(toolbar, "_colorButtons");
            var firstColor = colorButtons.First();
            var originalColor = vm.Toolbar.PenColor;
            firstColor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(originalColor, vm.Toolbar.PenColor);

            vm.Dispose();
            return session.DisposeAsync().AsTask();
        });
    }

    private static List<T> GetPrivateList<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field '{fieldName}' was not found.");

        return Assert.IsType<List<T>>(field.GetValue(instance));
    }
}
