using System.ComponentModel;
using System.Windows;

namespace Axinyp.Wpf.Hosting.Wpf.Internal;

internal sealed class DialogService(ViewCompositionFactory compositionFactory) : IDialogService
{
    public Task<IDialogResult> ShowDialogAsync<TViewModel>(Window owner, DialogParameters? parameters = null,
        CancellationToken cancellationToken = default) where TViewModel : class
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<IDialogResult>(cancellationToken);
        return owner.Dispatcher.InvokeAsync(() => Show(typeof(TViewModel), owner, parameters ?? [])).Task;
    }

    private IDialogResult Show(Type viewModelType, Window owner, DialogParameters parameters)
    {
        using var handle = compositionFactory.Create(viewModelType);
        if (handle.View is not Window window)
            throw new InvalidOperationException($"View '{handle.View.GetType().FullName}' 不是 Window，不能显示为 Dialog。");
        window.Owner = owner;
        IDialogResult result = DialogResult.Cancel();
        if (handle.ViewModel is IDialogAware aware)
        {
            IDialogResult? pendingClose = null;
            void ApplyClose(IDialogResult value)
            {
                if (!aware.CanCloseDialog()) return;
                result = value;
                if (!window.IsVisible)
                {
                    pendingClose = value;
                    return;
                }
                window.DialogResult = value.Result;
                window.Close();
            }

            EventHandler<IDialogResult>? close = (_, value) => ApplyClose(value);
            RoutedEventHandler? loaded = (_, _) =>
            {
                if (pendingClose is not null)
                    window.Dispatcher.BeginInvoke(() => ApplyClose(pendingClose));
            };
            CancelEventHandler? closing = (_, eventArgs) =>
            {
                if (!aware.CanCloseDialog()) eventArgs.Cancel = true;
            };
            aware.RequestClose += close;
            window.Loaded += loaded;
            window.Closing += closing;
            try { aware.OnDialogOpened(parameters); window.ShowDialog(); }
            finally
            {
                aware.RequestClose -= close;
                window.Loaded -= loaded;
                window.Closing -= closing;
                aware.OnDialogClosed();
            }
        }
        else result = new DialogResult(window.ShowDialog());
        return result;
    }
}
