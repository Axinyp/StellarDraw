using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhiteBoard.Client.Storage;
using WhiteBoard.UI.ViewModels;

namespace WhiteBoard.Client.ViewModels;

/// <summary>Client 主窗口的业务状态和白板文档命令。</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ILocalWhiteboardDocumentStore _documentStore;

    public MainWindowViewModel(WhiteboardViewModel board, ILocalWhiteboardDocumentStore documentStore)
    {
        Board = board;
        _documentStore = documentStore;
    }

    public WhiteboardViewModel Board { get; }

    public string Title => "StellarDraw · 白板";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [RelayCommand(CanExecute = nameof(CanOperateDocument))]
    private async Task SaveAsAsync()
    {
        await ExecuteDocumentOperationAsync(() => _documentStore.SaveAsAsync(Board));
    }

    [RelayCommand(CanExecute = nameof(CanOperateDocument))]
    private async Task OpenAsync()
    {
        await ExecuteDocumentOperationAsync(() => _documentStore.OpenAsync(Board));
    }

    private bool CanOperateDocument() => !IsBusy;

    private async Task ExecuteDocumentOperationAsync(Func<Task<DocumentOperationResult>> operation)
    {
        IsBusy = true;
        SaveAsCommand.NotifyCanExecuteChanged();
        OpenCommand.NotifyCanExecuteChanged();
        try
        {
            var result = await operation();
            StatusMessage = result.Message;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Client] Document command failed: {ex.GetType().Name}");
            StatusMessage = "文档操作失败，请重试。";
        }
        finally
        {
            IsBusy = false;
            SaveAsCommand.NotifyCanExecuteChanged();
            OpenCommand.NotifyCanExecuteChanged();
        }
    }
}
