using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WhiteBoard.Contracts.Services;
using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;
using WhiteBoard.UI.Rendering;

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

    public ICommand UndoCommand        { get; }
    public ICommand RedoCommand        { get; }
    public ICommand ClearPageCommand   { get; }
    public ICommand SelectCommand      { get; }
    public ICommand InsertShapeCommand { get; }
    public ICommand InsertImageCommand { get; }
    public ICommand AddPageCommand     { get; }

    /// <summary>已提交层需要重绘时在 UI 线程触发（由 Session 执行器线程通过 Dispatcher 转发）。</summary>
    public event Action? RedrawRequested;

    public WhiteboardViewModel(WhiteboardSession session, Dispatcher dispatcher)
    {
        _session    = session;
        _dispatcher = dispatcher;

        Toolbar = new ToolbarViewModel();
        Pages   = new PageListViewModel(session.Pages);

        UndoCommand        = new RelayCommand(Undo,    () => _session.History.CanUndo);
        RedoCommand        = new RelayCommand(Redo,    () => _session.History.CanRedo);
        ClearPageCommand   = new RelayCommand(ClearPage);
        SelectCommand      = new RelayCommand(() => Toolbar.CurrentTool = ToolType.Select);
        InsertShapeCommand = new RelayCommand<string>(InsertShape);
        InsertImageCommand = new RelayCommand(InsertImage);
        AddPageCommand     = new RelayCommand(AddPage);

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
            _subscribedPage.Cleared       -= OnPageCleared;
        }
        _subscribedPage = Pages.CurrentPage;
        if (_subscribedPage is not null)
        {
            _subscribedPage.ObjectAdded   += OnObjectChanged;
            _subscribedPage.ObjectRemoved += OnObjectChanged;
            _subscribedPage.Cleared       += OnPageCleared;
        }
    }

    private void OnObjectChanged(WhiteboardObject _)
        => _dispatcher.BeginInvoke(RequestRedraw, DispatcherPriority.Render);

    private void OnPageCleared()
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

    private void ClearPage()
    {
        _session.ClearCurrentPageAsync().ContinueWith(_ =>
            _dispatcher.BeginInvoke(RequestRedraw, DispatcherPriority.Render));
    }

    private void InsertShape(string? shapeType)
    {
        var type = shapeType?.ToLowerInvariant() switch
        {
            "circle"   => ShapeType.Circle,
            "triangle" => ShapeType.Triangle,
            "line"     => ShapeType.Line,
            "arrow"    => ShapeType.Arrow,
            _          => ShapeType.Rectangle,
        };
        var w = _session.CanvasWidth;
        var h = _session.CanvasHeight;
        var shape = new ShapeObject(type, 0xFFFFFFFF, 0xFF333333, 2f, true,
            w / 2 - 80, h / 2 - 60, 160, 120);
        _session.AddShapeAsync(shape).ContinueWith(_ =>
            _dispatcher.BeginInvoke(RequestRedraw, DispatcherPriority.Render));
    }

    private void InsertImage()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "选择图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource        = new Uri(dlg.FileName);
            bitmap.CacheOption      = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions    = BitmapCreateOptions.None;
            bitmap.EndInit();
            bitmap.Freeze();

            var raw     = System.IO.File.ReadAllBytes(dlg.FileName);
            var hash    = System.Security.Cryptography.SHA256.HashData(raw);
            var assetId = BitConverter.ToString(hash, 0, 8).Replace("-", "").ToLowerInvariant();

            ImageAssetStore.Store(assetId, bitmap);

            var maxW  = Math.Min(bitmap.PixelWidth, 600f);
            var scale = maxW / bitmap.PixelWidth;
            var imgW  = (float)(bitmap.PixelWidth  * scale);
            var imgH  = (float)(bitmap.PixelHeight * scale);
            var x     = (_session.CanvasWidth  - imgW) / 2;
            var y     = (_session.CanvasHeight - imgH) / 2;

            var image = new ImageObject(assetId, x, y, imgW, imgH);
            _session.AddImageAsync(image).ContinueWith(_ =>
                _dispatcher.BeginInvoke(RequestRedraw, DispatcherPriority.Render));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InsertImage] {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_subscribedPage is not null)
        {
            _subscribedPage.ObjectAdded   -= OnObjectChanged;
            _subscribedPage.ObjectRemoved -= OnObjectChanged;
            _subscribedPage.Cleared       -= OnPageCleared;
        }
        Pages.Dispose();
    }
}
