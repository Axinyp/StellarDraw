using System.Windows.Threading;
using WhiteBoard.Contracts.Services;
using WhiteBoard.Core.Domain;
using WhiteBoard.UI.ViewModels;

namespace WhiteBoard.UI.Tests;

public sealed class WhiteboardViewModelLifecycleTests
{
    [Fact]
    public async Task Dispose_UnhooksPageListPropertyChangedSubscription()
    {
        await Sta.RunAsync(() =>
        {
            var session = new WhiteboardSession(NullCollaborationAdapter.Instance);
            var viewModel = new WhiteboardViewModel(session, Dispatcher.CurrentDispatcher);

            viewModel.Dispose();

            var handlers = GetPropertyChangedHandlers(viewModel.Pages);
            Assert.Null(handlers);

            return session.DisposeAsync().AsTask();
        });
    }

    private static MulticastDelegate? GetPropertyChangedHandlers(object instance)
    {
        var type = instance.GetType();
        while (type is not null)
        {
            var field = type.GetField("PropertyChanged",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field is not null)
                return field.GetValue(instance) as MulticastDelegate;

            type = type.BaseType;
        }

        throw new InvalidOperationException("PropertyChanged backing field was not found.");
    }
}
