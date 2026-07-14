using System.Windows;

namespace WhiteBoard.Framework.Hosting.Wpf;

public interface IDialogService
{
    Task<IDialogResult> ShowDialogAsync<TViewModel>(Window owner, DialogParameters? parameters = null,
        CancellationToken cancellationToken = default) where TViewModel : class;
}
