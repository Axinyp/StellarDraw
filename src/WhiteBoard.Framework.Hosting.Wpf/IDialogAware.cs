namespace WhiteBoard.Framework.Hosting.Wpf;

public sealed class DialogParameters : Dictionary<string, object?>
{
    public DialogParameters() : base(StringComparer.Ordinal) { }
}

public interface IDialogAware
{
    event EventHandler<IDialogResult>? RequestClose;
    void OnDialogOpened(DialogParameters parameters);
    bool CanCloseDialog();
    void OnDialogClosed();
}
