using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WhiteBoard.Contracts.Services;
using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;

namespace WhiteBoard.UI.ViewModels;

/// <summary>
/// 白板主 ViewModel：连接 <see cref="WhiteboardSession"/>（领域层）与 WPF 视图层。
/// 负责工具栏状态、页面列表、笔画提交和橡皮擦操作，并将领域事件转换为 UI 重绘通知。
/// </summary>
public sealed class WhiteboardViewModel : ViewModelBase, IDisposable
{
    private readonly WhiteboardSession _session;
    private readonly Dispatcher        _dispatcher;

    public ToolbarViewModel  Toolbar { get; }
    public PageListViewModel Pages   { get; }

    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand AddPageCommand { get; }

    /// <summary>已提交层需要重绘时在 UI 线程触发（由 Session 执行器线程通过 Dispatcher 转发）。</summary>
    public event Action? RedrawRequested;

    public WhiteboardViewModel(WhiteboardSession session, Dispatcher dispatcher)
    {
        _session    = session;
        _dispatcher = dispatcher;

        Toolbar = new ToolbarViewModel();
        Pages   = new PageListViewModel(session.Pages);

        UndoCommand    = new RelayCommand(Undo,    () => _session.History.CanUndo);
        RedoCommand    = new RelayCommand(Redo,    () => _session.History.CanRedo);
        AddPageCommand = new RelayCommand(AddPage);

        SubscribeToCurrentPage();
        Pages.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PageListViewModel.CurrentPage))
                SubscribeToCurrentPage();
        };
    }

    public PageModel         CurrentPage => _session.Current;
    public IStrokePredictor? Predictor   => _session.Predictor;

    // ── Stroke commit ─────────────────────────────────────────────────────────

    public async Task CommitStrokeAsync(IReadOnlyList<PressurePoint> points)
    {
        if (points.Count < 2) return;

        var style  = BuildStyle();
        var stroke = new StrokeObject(points, style); // 使用自动计算 Bounds 的构造函数

        await _session.AddStrokeAsync(stroke);
        RequestRedraw();
    }

    public async Task EraseAtAsync(float docX, float docY)
    {
        await _session.EraseAtAsync(docX, docY);
        RequestRedraw();
    }

    // ── Undo / Redo ──────────────────────────────────────────────────────────

    private void Undo()
    {
        _session.PostOperationAsync(() =>
        {
            _session.History.Undo();
        }).AsTask().ContinueWith(_ => RequestRedraw(),
            TaskContinuationOptions.ExecuteSynchronously);
    }

    private void Redo()
    {
        _session.PostOperationAsync(() =>
        {
            _session.History.Redo();
        }).AsTask().ContinueWith(_ => RequestRedraw(),
            TaskContinuationOptions.ExecuteSynchronously);
    }

    private void AddPage()
    {
        Pages.AddPage();
        SubscribeToCurrentPage();
        RequestRedraw();
    }

    // ── Change notification ───────────────────────────────────────────────────

    private PageModel? _subscribedPage;

    private void SubscribeToCurrentPage()
    {
        if (_subscribedPage is not null)
        {
            _subscribedPage.ObjectAdded   -= OnObjectChanged;
            _subscribedPage.ObjectRemoved -= OnObjectChanged;
        }
        _subscribedPage = Pages.CurrentPage;
        if (_subscribedPage is not null)
        {
            _subscribedPage.ObjectAdded   += OnObjectChanged;
            _subscribedPage.ObjectRemoved += OnObjectChanged;
        }
    }

    private void OnObjectChanged(WhiteboardObject _)
        => _dispatcher.BeginInvoke(RequestRedraw, DispatcherPriority.Render);

    private void RequestRedraw() => RedrawRequested?.Invoke();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private StrokeStyle BuildStyle()
    {
        var penType = Toolbar.CurrentTool switch
        {
            ToolType.Highlighter => PenType.Highlighter,
            ToolType.Marker      => PenType.Marker,
            _                    => PenType.Pen,
        };
        return new StrokeStyle(
            penType,
            InkType.Normal,
            Toolbar.PenColor,
            Toolbar.Thickness,
            IsFilled:    false,
            IsAntiAlias: Toolbar.IsAntiAlias);
    }

    public void Dispose()
    {
        if (_subscribedPage is not null)
        {
            _subscribedPage.ObjectAdded   -= OnObjectChanged;
            _subscribedPage.ObjectRemoved -= OnObjectChanged;
        }
        Pages.Dispose();
    }
}
