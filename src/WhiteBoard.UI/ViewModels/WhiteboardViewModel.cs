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
    private readonly Dispatcher _dispatcher;

    public ToolbarViewModel Toolbar { get; }
    public PageListViewModel Pages { get; }

    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand ClearPageCommand { get; }
    public ICommand SelectCommand { get; }
    public ICommand InsertShapeCommand { get; }
    public ICommand InsertImageCommand { get; }
    public ICommand AddPageCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand CopySelectedCommand { get; }
    public ICommand BringToFrontCommand { get; }

    /// <summary>已提交层需要重绘时在 UI 线程触发（由 Session 执行器线程通过 Dispatcher 转发）。</summary>
    public event Action? RedrawRequested;

    /// <summary>选中集合发生变化时触发（供 InkSurface 更新选择覆盖层）。</summary>
    public event Action? SelectionChanged;

    public WhiteboardViewModel(WhiteboardSession session, Dispatcher dispatcher)
    {
        _session = session;
        _dispatcher = dispatcher;

        Toolbar = new ToolbarViewModel();
        Pages = new PageListViewModel(session.Pages);

        UndoCommand = new RelayCommand(Undo, () => _session.History.CanUndo);
        RedoCommand = new RelayCommand(Redo, () => _session.History.CanRedo);
        ClearPageCommand = new RelayCommand(ClearPage);
        SelectCommand = new RelayCommand(() => Toolbar.CurrentTool = ToolType.Select);
        InsertShapeCommand = new RelayCommand<string>(InsertShape);
        InsertImageCommand = new RelayCommand(InsertImage);
        AddPageCommand = new RelayCommand(AddPage);
        DeleteSelectedCommand = new RelayCommand(() => _ = DeleteSelectedAsync());
        CopySelectedCommand = new RelayCommand(() => _ = CopySelectedAsync());
        BringToFrontCommand = new RelayCommand(() => _ = BringToFrontSelectedAsync());

        SubscribeToCurrentPage();
        Pages.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PageListViewModel.CurrentPage))
                SubscribeToCurrentPage();
        };
    }

    public PageModel CurrentPage => _session.Current;
    public IStrokePredictor? Predictor => _session.Predictor;
    public SelectionModel Selection => _session.Selection;

    public void NotifySelectionChanged()
        => _dispatcher.BeginInvoke(() => SelectionChanged?.Invoke(), DispatcherPriority.Render);

    // ── Stroke commit ─────────────────────────────────────────────────────────

    public async Task CommitStrokeAsync(IReadOnlyList<PressurePoint> points)
    {
        if (points.Count < 2) return;

        var style = BuildStyle();
        var stroke = new StrokeObject(points, style); // 使用自动计算 Bounds 的构造函数

        await _session.AddStrokeAsync(stroke);
        RequestRedraw();
    }

    public async Task EraseAtAsync(float docX, float docY)
    {
        await _session.EraseAtAsync(docX, docY);
        RequestRedraw();
    }

    // ── Selection operations ──────────────────────────────────────────────────

    public async Task SelectRegionAsync(float x, float y, float w, float h)
    {
        await _session.SelectObjectsInRegionAsync(x, y, w, h);
        NotifySelectionChanged();
    }

    public async Task MoveSelectedAsync(float dx, float dy)
    {
        var ids = _session.Selection.ToList();
        if (ids.Count == 0) return;
        await _session.MoveObjectsAsync(ids, dx, dy);
        RequestRedraw();
    }

    public void ClearSelection()
    {
        _session.Selection.Clear();
        NotifySelectionChanged();
    }

    public async Task DeleteSelectedAsync()
    {
        var ids = _session.Selection.ToList();
        _session.Selection.Clear();
        if (ids.Count > 0)
            await _session.DeleteObjectsAsync(ids);
        RequestRedraw();
        NotifySelectionChanged();
    }

    public async Task CopySelectedAsync()
    {
        var ids = _session.Selection.ToList();
        if (ids.Count == 0) return;
        var newIds = await _session.CopyObjectsAsync(ids);
        _session.Selection.SelectAll(newIds);
        RequestRedraw();
        NotifySelectionChanged();
    }

    public async Task BringToFrontSelectedAsync()
    {
        var ids = _session.Selection.ToList();
        if (ids.Count == 0) return;
        await _session.BringToFrontAsync(ids);
        RequestRedraw();
    }

    public async Task ResizeBoundsAsync(IReadOnlyList<(Guid Id, float X, float Y, float W, float H)> updates)
    {
        await _session.ResizeBoundsAsync(updates);
        RequestRedraw();
    }

    public async Task RotateSelectedAsync(float angleDeg, float pivotDocX, float pivotDocY)
    {
        var ids = _session.Selection.ToList();
        if (ids.Count == 0) return;
        await _session.RotateObjectsAsync(ids, angleDeg, pivotDocX, pivotDocY);
        RequestRedraw();
    }

    // ── Undo / Redo ──────────────────────────────────────────────────────────

    private void Undo()
    {
        _session.PostOperationAsync(() =>
        {
            _session.History.Undo();
        }).AsTask().ContinueWith(_ =>
            // 续体可能在执行器线程触发，重绘涉及 DispatcherObject，必须 marshal 回 UI 线程。
            _dispatcher.BeginInvoke(RequestRedraw, DispatcherPriority.Render));
    }

    private void Redo()
    {
        _session.PostOperationAsync(() =>
        {
            _session.History.Redo();
        }).AsTask().ContinueWith(_ =>
            // 同 Undo：避免在执行器线程直接触碰 UI 渲染。
            _dispatcher.BeginInvoke(RequestRedraw, DispatcherPriority.Render));
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
            _subscribedPage.ObjectAdded -= OnObjectChanged;
            _subscribedPage.ObjectRemoved -= OnObjectChanged;
            _subscribedPage.Cleared -= OnPageCleared;
        }
        _subscribedPage = Pages.CurrentPage;
        if (_subscribedPage is not null)
        {
            _subscribedPage.ObjectAdded += OnObjectChanged;
            _subscribedPage.ObjectRemoved += OnObjectChanged;
            _subscribedPage.Cleared += OnPageCleared;
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
            ToolType.Marker => PenType.Marker,
            _ => PenType.Pen,
        };
        return new StrokeStyle(
            penType,
            InkType.Normal,
            Toolbar.PenColor,
            Toolbar.Thickness,
            IsFilled: false,
            IsAntiAlias: Toolbar.IsAntiAlias);
    }

    private void ClearPage()
    {
        _session.ClearCurrentPageAsync().ContinueWith(_ =>
            _dispatcher.BeginInvoke(RequestRedraw, DispatcherPriority.Render));
    }

    private void InsertShape(string? shapeType)
    {
        var w = _session.CanvasWidth;
        var h = _session.CanvasHeight;
        InsertShapeAt(shapeType, w / 2 - 80, h / 2 - 60, 160, 120);
    }

    /// <summary>
    /// 在指定文档矩形内插入形状（供画布拖拽绘制使用）。
    /// 由 InkSurface 在 Shape 工具下拖拽松开时调用，传入用户拉出的位置与大小。
    /// </summary>
    public void InsertShapeAt(string? shapeType, float x, float y, float w, float h)
    {
        var shape = new ShapeObject(ParseShapeType(shapeType), 0xFFFFFFFF, 0xFF333333, 2f, true,
            x, y, w, h);
        _session.AddShapeAsync(shape).ContinueWith(_ =>
            _dispatcher.BeginInvoke(RequestRedraw, DispatcherPriority.Render));
    }

    /// <summary>将形状类型字符串解析为 <see cref="ShapeType"/>，未知值回退为矩形（UI 只传合法值）。</summary>
    public static ShapeType ParseShapeType(string? shapeType) =>
        ShapeTypeParser.TryParse(shapeType, out var t) ? t : ShapeType.Rectangle;

    private void InsertImage()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(dlg.FileName);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.None;
            bitmap.EndInit();
            bitmap.Freeze();

            var raw = System.IO.File.ReadAllBytes(dlg.FileName);
            var hash = System.Security.Cryptography.SHA256.HashData(raw);
            var assetId = BitConverter.ToString(hash, 0, 8).Replace("-", "").ToLowerInvariant();

            ImageAssetStore.Store(assetId, bitmap);

            var maxW = Math.Min(bitmap.PixelWidth, 600f);
            var scale = maxW / bitmap.PixelWidth;
            var imgW = (float)(bitmap.PixelWidth * scale);
            var imgH = (float)(bitmap.PixelHeight * scale);
            var x = (_session.CanvasWidth - imgW) / 2;
            var y = (_session.CanvasHeight - imgH) / 2;

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
            _subscribedPage.ObjectAdded -= OnObjectChanged;
            _subscribedPage.ObjectRemoved -= OnObjectChanged;
            _subscribedPage.Cleared -= OnPageCleared;
        }
        Pages.Dispose();
    }
}