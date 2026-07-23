using System.Windows.Input;
using System.Windows.Threading;
using WhiteBoard.Contracts.Services;
using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;
using WhiteBoard.Core.Services;
using WhiteBoard.UI.Rendering;

namespace WhiteBoard.UI.ViewModels;

/// <summary>
/// 白板主 ViewModel：只消费 <see cref="WhiteboardSession"/> 在执行器内捕获的不可变快照。
/// 页面对象仅在 UI 线程从快照反序列化，用于渲染与交互预览，绝不回写 Session。
/// </summary>
public sealed class WhiteboardViewModel : ViewModelBase, IDisposable
{
    private readonly WhiteboardSession _session;
    private readonly Dispatcher _dispatcher;
    private readonly RelayCommand _undoCommand;
    private readonly RelayCommand _redoCommand;
    private readonly HashSet<Guid> _selectedObjectIds = [];
    private PageModel? _currentRenderPage;
    private CanvasSizeSnapshot _canvasSize = new(1920f, 1080f);
    private HistoryStateSnapshot _history = new(false, false);
    private long _appliedRevision = -1;
    private long _requestedRevision;
    private int _refreshQueued;
    private bool _disposed;

    public ToolbarViewModel Toolbar { get; }
    public PageListViewModel Pages { get; }

    public ICommand UndoCommand => _undoCommand;
    public ICommand RedoCommand => _redoCommand;
    public ICommand ClearPageCommand { get; }
    public ICommand SelectCommand { get; }
    public ICommand InsertShapeCommand { get; }
    public ICommand InsertImageCommand { get; }
    public ICommand AddPageCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand CopySelectedCommand { get; }
    public ICommand BringToFrontCommand { get; }

    /// <summary>已提交层需要重绘时在 UI 线程触发。</summary>
    public event Action? RedrawRequested;

    /// <summary>选中集合发生变化时触发（供 InkSurface 更新选择覆盖层）。</summary>
    public event Action? SelectionChanged;

    public WhiteboardViewModel(WhiteboardSession session, Dispatcher dispatcher)
    {
        _session = session;
        _dispatcher = dispatcher;

        Toolbar = new ToolbarViewModel();
        Pages = new PageListViewModel();
        _undoCommand = new RelayCommand(() => _ = UndoAsync(), () => _history.CanUndo);
        _redoCommand = new RelayCommand(() => _ = RedoAsync(), () => _history.CanRedo);
        ClearPageCommand = new RelayCommand(() => _ = ClearPageAsync());
        SelectCommand = new RelayCommand(() => Toolbar.CurrentTool = ToolType.Select);
        InsertShapeCommand = new RelayCommand<string>(InsertShape);
        InsertImageCommand = new RelayCommand(InsertImage);
        AddPageCommand = new RelayCommand(() => _ = AddPageAsync());
        DeleteSelectedCommand = new RelayCommand(() => _ = DeleteSelectedAsync());
        CopySelectedCommand = new RelayCommand(() => _ = CopySelectedAsync());
        BringToFrontCommand = new RelayCommand(() => _ = BringToFrontSelectedAsync());

        _session.StateChanged += OnSessionStateChanged;
        QueueRefresh();
    }

    /// <summary>仅供 UI 渲染与预览使用的页面副本。</summary>
    public PageModel? CurrentRenderPage => _currentRenderPage;

    /// <summary>仅供 UI 交互使用的当前选择 ID 副本。</summary>
    public IReadOnlyList<Guid> SelectedObjectIds => _selectedObjectIds.ToArray();

    public bool HasSelection => _selectedObjectIds.Count > 0;

    public CanvasSizeSnapshot CanvasSize => _canvasSize;

    public IStrokePredictor? Predictor => _session.Predictor;

    public Task SetCanvasSizeAsync(float width, float height, CancellationToken ct = default) =>
        _session.SetCanvasSizeAsync(width, height, ct);

    /// <summary>捕获完整白板文档快照，供宿主在可信文件边界完成保存。</summary>
    public Task<WhiteboardDocumentSnapshot> CaptureDocumentAsync(CancellationToken ct = default) =>
        _session.CaptureDocumentAsync(ct);

    /// <summary>替换完整白板文档；状态通知会统一触发快照刷新。</summary>
    public async Task ReplaceDocumentAsync(WhiteboardDocumentSnapshot document, CancellationToken ct = default)
    {
        await _session.ReplaceDocumentAsync(document, ct);
        await RefreshUiSnapshotAsync(ct);
    }

    public async Task<bool> GoToPageAsync(string pageId, CancellationToken ct = default)
    {
        var changed = await _session.GoToPageAsync(pageId, ct);
        if (changed)
            await RefreshUiSnapshotAsync(ct);
        return changed;
    }

    public async Task CommitStrokeAsync(IReadOnlyList<PressurePoint> points)
    {
        if (points.Count < 2)
            return;

        await _session.AddStrokeAsync(new StrokeObject(points, BuildStyle()));
        await RefreshUiSnapshotAsync();
    }

    public async Task EraseAtAsync(float docX, float docY)
    {
        await _session.EraseAtAsync(docX, docY);
        await RefreshUiSnapshotAsync();
    }

    public async Task SelectRegionAsync(float x, float y, float w, float h)
    {
        await _session.SelectObjectsInRegionAsync(x, y, w, h);
        await RefreshUiSnapshotAsync();
    }

    public async Task MoveSelectedAsync(float dx, float dy)
    {
        var ids = SelectedObjectIds;
        if (ids.Count == 0)
            return;

        await _session.MoveObjectsAsync(ids, dx, dy);
        await RefreshUiSnapshotAsync();
    }

    public async Task ClearSelectionAsync()
    {
        await _session.ClearSelectionAsync();
        await RefreshUiSnapshotAsync();
    }

    public async Task DeleteSelectedAsync()
    {
        var ids = SelectedObjectIds;
        if (ids.Count == 0)
            return;

        await _session.DeleteObjectsAsync(ids);
        await RefreshUiSnapshotAsync();
    }

    public async Task CopySelectedAsync()
    {
        var ids = SelectedObjectIds;
        if (ids.Count == 0)
            return;

        var result = await _session.CopyObjectsAsync(ids);
        if (result.Changed)
            await _session.SetSelectedObjectIdsAsync(result.AffectedObjectIds);
        await RefreshUiSnapshotAsync();
    }

    public async Task BringToFrontSelectedAsync()
    {
        var ids = SelectedObjectIds;
        if (ids.Count == 0)
            return;

        await _session.BringToFrontAsync(ids);
        await RefreshUiSnapshotAsync();
    }

    public async Task ResizeBoundsAsync(IReadOnlyList<(Guid Id, float X, float Y, float W, float H)> updates)
    {
        await _session.ResizeBoundsAsync(updates);
        await RefreshUiSnapshotAsync();
    }

    public async Task RotateSelectedAsync(float angleDeg, float pivotDocX, float pivotDocY)
    {
        var ids = SelectedObjectIds;
        if (ids.Count == 0)
            return;

        await _session.RotateObjectsAsync(ids, angleDeg, pivotDocX, pivotDocY);
        await RefreshUiSnapshotAsync();
    }

    public void NotifySelectionChanged() =>
        _dispatcher.BeginInvoke(NotifySelectionChangedCore, DispatcherPriority.Render);

    private async Task UndoAsync()
    {
        await _session.UndoAsync();
        await RefreshUiSnapshotAsync();
    }

    private async Task RedoAsync()
    {
        await _session.RedoAsync();
        await RefreshUiSnapshotAsync();
    }

    private async Task ClearPageAsync()
    {
        await _session.ClearCurrentPageAsync();
        await RefreshUiSnapshotAsync();
    }

    private async Task AddPageAsync()
    {
        await _session.AddPageAsync();
        await RefreshUiSnapshotAsync();
    }

    private void InsertShape(string? shapeType) => _ = InsertShapeAsync(shapeType);

    private async Task InsertShapeAsync(string? shapeType)
    {
        var size = _canvasSize;
        await InsertShapeAtAsync(shapeType, size.Width / 2 - 80, size.Height / 2 - 60, 160, 120);
    }

    /// <summary>在指定文档矩形内插入形状（供画布拖拽绘制使用）。</summary>
    public void InsertShapeAt(string? shapeType, float x, float y, float w, float h) =>
        _ = InsertShapeAtAsync(shapeType, x, y, w, h);

    private async Task InsertShapeAtAsync(string? shapeType, float x, float y, float w, float h)
    {
        var shape = new ShapeObject(ParseShapeType(shapeType), 0xFFFFFFFF, 0xFF333333, 2f, true,
            x, y, w, h);
        await _session.AddShapeAsync(shape);
        await RefreshUiSnapshotAsync();
    }

    /// <summary>将形状类型字符串解析为 <see cref="ShapeType"/>，未知值回退为矩形。</summary>
    public static ShapeType ParseShapeType(string? shapeType) =>
        ShapeTypeParser.TryParse(shapeType, out var type) ? type : ShapeType.Rectangle;

    private void InsertImage() => _ = InsertImageAsync();

    private async Task InsertImageAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*",
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var fileInfo = new System.IO.FileInfo(dialog.FileName);
            if (fileInfo.Length <= 0 || fileInfo.Length > ImageAssetValidator.MaxCompressedBytes)
                return;

            var raw = await System.IO.File.ReadAllBytesAsync(dialog.FileName);
            if (!ImageAssetValidator.TryValidate(raw, out _))
                return;

            var bitmap = InkRenderer.DecodeBitmap(raw);
            if (bitmap is null)
                return;

            var assetId = RawImageAssetStore.ComputeAssetId(raw);
            if (!RawImageAssetStore.TryStore(assetId, raw))
                return;
            ImageAssetStore.Store(assetId, bitmap);

            var maxWidth = Math.Min(bitmap.PixelWidth, 600f);
            var scale = maxWidth / bitmap.PixelWidth;
            var imageWidth = (float)(bitmap.PixelWidth * scale);
            var imageHeight = (float)(bitmap.PixelHeight * scale);
            var size = _canvasSize;
            await _session.AddImageAsync(new ImageObject(assetId,
                (size.Width - imageWidth) / 2,
                (size.Height - imageHeight) / 2,
                imageWidth,
                imageHeight));
            await RefreshUiSnapshotAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InsertImage] {ex.Message}");
        }
    }

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

    private void OnSessionStateChanged(long revision)
    {
        UpdateRequestedRevision(revision);
        QueueRefresh();
    }

    private void UpdateRequestedRevision(long revision)
    {
        long observed;
        do
        {
            observed = Volatile.Read(ref _requestedRevision);
            if (observed >= revision)
                return;
        }
        while (Interlocked.CompareExchange(ref _requestedRevision, revision, observed) != observed);
    }

    private void QueueRefresh()
    {
        if (_disposed || Interlocked.Exchange(ref _refreshQueued, 1) != 0)
            return;

        _dispatcher.BeginInvoke(async () =>
        {
            try
            {
                do
                {
                    await RefreshUiSnapshotAsync();
                }
                while (!_disposed &&
                    Volatile.Read(ref _appliedRevision) < Volatile.Read(ref _requestedRevision));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WhiteboardViewModel] Refresh failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _refreshQueued, 0);
                if (!_disposed && Volatile.Read(ref _appliedRevision) < Volatile.Read(ref _requestedRevision))
                    QueueRefresh();
            }
        }, DispatcherPriority.Render);
    }

    private async Task RefreshUiSnapshotAsync(CancellationToken ct = default)
    {
        var snapshot = await _session.CaptureUiSnapshotAsync(ct);
        if (!_dispatcher.CheckAccess())
        {
            await _dispatcher.InvokeAsync(() => ApplySnapshot(snapshot), DispatcherPriority.Render, ct);
            return;
        }

        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(SessionUiSnapshot snapshot)
    {
        if (snapshot.Revision < _appliedRevision)
            return;

        _appliedRevision = snapshot.Revision;
        UpdateRequestedRevision(snapshot.Revision);
        _canvasSize = snapshot.CanvasSize;
        _history = snapshot.History;
        Pages.Apply(snapshot.Pages);
        _currentRenderPage = PageSerializer.Deserialize(
            snapshot.CurrentPage.Data,
            snapshot.CurrentPage.PageId,
            snapshot.CurrentPage.Title);
        _currentRenderPage.Background = snapshot.CurrentPage.Background;

        _selectedObjectIds.Clear();
        foreach (var id in snapshot.SelectedObjectIds)
            _selectedObjectIds.Add(id);

        _undoCommand.RaiseCanExecuteChanged();
        _redoCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CurrentRenderPage));
        OnPropertyChanged(nameof(SelectedObjectIds));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanvasSize));
        RedrawRequested?.Invoke();
        NotifySelectionChangedCore();
    }

    private void NotifySelectionChangedCore() => SelectionChanged?.Invoke();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _session.StateChanged -= OnSessionStateChanged;
        Pages.Dispose();
    }
}
