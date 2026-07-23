using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WhiteBoard.Contracts.Services;
using WhiteBoard.UI.Rendering;
using WhiteBoard.UI.ViewModels;

namespace WhiteBoard.UI.Controls;

/// <summary>
/// L1 延迟层：三 DrawingVisual 分离已提交笔画、湿墨迹、前向预测笔迹。
///
/// 输入分层（三通道，互不干扰）：
///   A. OnTouchDown/Move/Up  ── 单指触摸绘图（不使用 StylusCapture，避免与 Manipulation 冲突）
///   B. OnManipulation*      ── 双指捏合缩放 / 平移（IsManipulationEnabled = true）
///   C. OnMouseLeft*         ── 纯鼠标设备兜底（StylusDevice == null 时才执行）
///
/// 关键规则：
///   - 触摸通道（A）与 Manipulation（B）互斥：OnTouchDown 中单指捕获时
///     调用 e.Handled = true，阻止 Manipulation 对单指的干预；
///     第二根手指落下时主动中止绘图，Manipulation 接管。
///   - 永远不调用 e.StylusDevice.Capture()：这会与 ManipulationSystem 产生递归。
/// </summary>
public sealed class InkSurface : FrameworkElement
{
    private readonly DrawingVisual _committedVisual = new();
    private readonly DrawingVisual _wetInkVisual = new();
    private readonly DrawingVisual _predictedVisual = new();
    private readonly DrawingVisual _selectionVisual = new();   // 选择覆盖层（屏幕空间，无视图变换）
    private readonly DrawingVisual _latencyVisual = new();   // 延迟 Overlay（仅 ShowLatency=true 时使用）
    private readonly VisualCollection _visuals;
    private readonly MatrixTransform _viewMatrixTransform = new();
    private readonly Canvas _overlayCanvas = new() { IsHitTestVisible = true }; // 浮动工具栏宿主

    // 拖动期间从 committed 层隐藏的对象 id（覆盖层实时绘制其跟手图形，避免与原件重影）
    private readonly HashSet<Guid> _hiddenObjectIds = new();

    private Matrix _viewMatrix = Matrix.Identity;

    // 延迟测量
    private long _lastTouchTimestampMs;      // 最近一次 OnTouchMove/OnMouseMove 时的时间戳

    private bool _isDrawing;                 // 是否正在绘制（用于激活帧计时）
    private readonly LatencyStats _latencyStats = new();

    // 帧间预测平滑：保留上一帧结果做 EMA 混合，消除逐帧位置跳变
    private IReadOnlyList<PredictedPoint>? _prevPredicted;

    // 绘图状态
    private List<PressurePoint>? _currentPoints;

    // Shape 工具：拖拽绘制形状的起点（文档坐标），null 表示当前未在绘制形状
    private Point? _shapeStartDoc;

    private int _primaryTouchId = -1;
    private int _activeTouchCount;

    // 鼠标平移状态
    private bool _isPanning;

    private Point _panStartScreen;
    private Matrix _panStartMatrix;

    // Manipulation 状态
    private bool _isManipulating;

    // 选择控制器
    private SelectionController? _controller;

    public InkSurface()
    {
        _visuals = new VisualCollection(this)
            { _committedVisual, _wetInkVisual, _predictedVisual, _selectionVisual, _latencyVisual };
        _committedVisual.Transform = _viewMatrixTransform;
        _wetInkVisual.Transform = _viewMatrixTransform;
        _predictedVisual.Transform = _viewMatrixTransform;
        // _selectionVisual 和 _latencyVisual 均为屏幕空间，不附加视图变换

        // _overlayCanvas 作为额外 UIElement 子级挂载（用于浮动工具栏按钮）
        AddVisualChild(_overlayCanvas);
        AddLogicalChild(_overlayCanvas);

        Focusable = true;
        ClipToBounds = true;
        IsManipulationEnabled = true;           // 启用双指操作
        Stylus.SetIsFlicksEnabled(this, false); // 禁用划动手势，防止误触

        Loaded += (_, _) => RenderCommitted();
    }

    // ── ShowLatency DependencyProperty ───────────────────────────────────────

    public static readonly DependencyProperty ShowLatencyProperty =
        DependencyProperty.Register(nameof(ShowLatency), typeof(bool),
            typeof(InkSurface), new PropertyMetadata(false, OnShowLatencyChanged));

    /// <summary>
    /// 设为 true 时在画布左上角显示实时帧延迟（touch→frame ms）统计 Overlay。
    /// 可在 XAML 或运行时切换，不影响正常绘图流程。
    /// </summary>
    public bool ShowLatency
    {
        get => (bool)GetValue(ShowLatencyProperty);
        set => SetValue(ShowLatencyProperty, value);
    }

    private static void OnShowLatencyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var s = (InkSurface)d;
        if ((bool)e.NewValue)
            CompositionTarget.Rendering += s.OnRendering;
        else
        {
            CompositionTarget.Rendering -= s.OnRendering;
            using (s._latencyVisual.RenderOpen()) { }  // clear overlay
        }
    }

    // 每帧回调：在绘制期间计算 touch→frame 延迟并绘制 Overlay
    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_isDrawing) return;

        long nowMs = Environment.TickCount64;
        long lagMs = nowMs - _lastTouchTimestampMs;
        if (lagMs < 0 || lagMs > 500) return;   // 异常值忽略

        _latencyStats.Record((double)lagMs);

        var typeface = new Typeface("Consolas");
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var lines = new[]
        {
            $"Frame lag : {lagMs,4} ms",
            $"Avg (10)  : {_latencyStats.Avg,5:F1} ms",
            $"P95       : {_latencyStats.P95,5:F1} ms",
            $"Samples   : {_latencyStats.Count,5}",
        };

        using var dc = _latencyVisual.RenderOpen();
        double y = 8;
        foreach (var line in lines)
        {
            var ft = new FormattedText(line,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface, 13, Brushes.White, dpi);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), null,
                new Rect(6, y - 1, ft.Width + 8, ft.Height + 2));
            dc.DrawText(ft, new Point(10, y));
            y += ft.Height + 3;
        }
    }

    // ── DependencyProperty: ViewModel ────────────────────────────────────────

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(WhiteboardViewModel),
            typeof(InkSurface), new PropertyMetadata(null, OnViewModelChanged));

    public WhiteboardViewModel? ViewModel
    {
        get => (WhiteboardViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var s = (InkSurface)d;
        if (e.OldValue is WhiteboardViewModel old)
        {
            old.RedrawRequested -= s.RenderCommitted;
            old.SelectionChanged -= s.OnSelectionChanged;
        }
        if (e.NewValue is WhiteboardViewModel vm)
        {
            s._controller = new SelectionController(s._selectionVisual, vm, () => s._viewMatrix, s._overlayCanvas,
                ids => s.SetHiddenObjects(ids));
            vm.RedrawRequested += s.RenderCommitted;
            vm.SelectionChanged += s.OnSelectionChanged;
            s.RenderCommitted();
        }
    }

    private void OnSelectionChanged() => _controller?.RenderOverlay();

    // ── Visual tree ──────────────────────────────────────────────────────────

    // _overlayCanvas 作为额外 UIElement 子级（不在 VisualCollection 中），需单独处理
    protected override int VisualChildrenCount => _visuals.Count + 1;

    protected override Visual GetVisualChild(int index)
        => index < _visuals.Count ? _visuals[index] : _overlayCanvas;

    protected override Size MeasureOverride(Size availableSize)
    {
        _overlayCanvas.Measure(availableSize);
        return new Size(0, 0); // InkSurface 填满父容器，由父元素决定尺寸
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _overlayCanvas.Arrange(new Rect(finalSize));
        return finalSize;
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    public void RenderCommitted()
    {
        using var dc = _committedVisual.RenderOpen();
        var vm = ViewModel;
        if (vm is null) return;
        var page = vm.CurrentRenderPage;
        if (page is null) return;
        InkRenderer.RenderPage(dc, page, RenderSize,
            _hiddenObjectIds.Count > 0 ? _hiddenObjectIds : null);
        // 页面重绘后同步更新选择覆盖层（对象移动/删除后 bounds 已更新）
        _controller?.RenderOverlay();
    }

    /// <summary>
    /// 设置拖动期间需从 committed 层隐藏的对象（覆盖层会实时绘制其跟手图形）。
    /// 传 null/空集合表示恢复全部显示。设置后立即重绘 committed 层。
    /// </summary>
    internal void SetHiddenObjects(IReadOnlyCollection<Guid>? ids)
    {
        _hiddenObjectIds.Clear();
        if (ids is not null)
            foreach (var id in ids) _hiddenObjectIds.Add(id);
        RenderCommitted();
    }

    private void RenderWetInk()
    {
        var vm = ViewModel;
        var pts = _currentPoints;
        using (var dc = _wetInkVisual.RenderOpen())
        {
            if (pts is { Count: >= 2 } && vm is not null)
                InkRenderer.RenderWetStroke(dc, pts, vm.Toolbar.PenColor, vm.Toolbar.Thickness);
        }
        RenderPredictedInk(vm, pts);
    }

    private void RenderPredictedInk(WhiteboardViewModel? vm, List<PressurePoint>? pts)
    {
        if (vm?.Predictor is not { } predictor || pts is not { Count: >= 2 })
        {
            _prevPredicted = null;
            ClearPredictedInk();
            return;
        }

        var raw = predictor.Predict(pts, lookaheadMs: 16f, isStrokeBegin: pts.Count <= 3);

        // 帧间 EMA 平滑（α=0.60）：新帧 60%，上一帧 40%，消除位置跳变
        var predicted = BlendPredictions(raw, _prevPredicted, alpha: 0.60f);
        _prevPredicted = predicted;

        using var dc = _predictedVisual.RenderOpen();
        if (predicted.Count > 0)
            PredictedInkRenderer.RenderPredictedStroke(dc, predicted, pts,
                vm.Toolbar.PenColor, vm.Toolbar.Thickness);
    }

    // 将两帧预测结果做逐点 EMA 混合，长度取较短者
    private static IReadOnlyList<PredictedPoint> BlendPredictions(
        IReadOnlyList<PredictedPoint> current,
        IReadOnlyList<PredictedPoint>? previous,
        float alpha)
    {
        if (previous is null || previous.Count == 0 || current.Count == 0)
            return current;

        int count = Math.Min(current.Count, previous.Count);
        float beta = 1f - alpha;
        var blended = new PredictedPoint[count];
        for (int i = 0; i < count; i++)
        {
            blended[i] = new PredictedPoint(
                X: alpha * current[i].X + beta * previous[i].X,
                Y: alpha * current[i].Y + beta * previous[i].Y,
                Pressure: current[i].Pressure,
                Confidence: current[i].Confidence);
        }
        return blended;
    }

    private void ClearWetInk()
    { using (_wetInkVisual.RenderOpen()) { } }

    // ── Shape 工具：拖拽绘制形状 ────────────────────────────────────────────────

    /// <summary>在湿墨迹层（文档空间）绘制形状预览，起点 _shapeStartDoc → 当前点构成矩形。</summary>
    private void RenderShapePreview(Point currentDoc)
    {
        if (_shapeStartDoc is not { } start) return;
        var vm = ViewModel;
        if (vm is null) return;

        float x = (float)Math.Min(start.X, currentDoc.X);
        float y = (float)Math.Min(start.Y, currentDoc.Y);
        float w = (float)Math.Abs(currentDoc.X - start.X);
        float h = (float)Math.Abs(currentDoc.Y - start.Y);

        using var dc = _wetInkVisual.RenderOpen();
        if (w < 1 || h < 1) return;
        // 半透明蓝填充 + 蓝边作预览提示
        var preview = new WhiteBoard.Core.Domain.Objects.ShapeObject(
            WhiteboardViewModel.ParseShapeType(vm.Toolbar.PendingShapeType),
            0x330078D4u, 0xFF0078D4u, 1.5f, true, x, y, w, h);
        InkRenderer.RenderObject(dc, preview);
    }

    /// <summary>松开时按拖出的矩形提交形状；拖拽过小视为点击，用默认大小居中于落点。</summary>
    private void CommitShape(Point currentDoc)
    {
        if (_shapeStartDoc is not { } start) return;
        var vm = ViewModel;
        _shapeStartDoc = null;
        ClearWetInk();
        if (vm is null) return;

        float x = (float)Math.Min(start.X, currentDoc.X);
        float y = (float)Math.Min(start.Y, currentDoc.Y);
        float w = (float)Math.Abs(currentDoc.X - start.X);
        float h = (float)Math.Abs(currentDoc.Y - start.Y);

        if (w < 5 || h < 5)   // 未拖拽（点击）→ 默认尺寸居中于落点
        {
            w = 160; h = 120;
            x = (float)currentDoc.X - w / 2;
            y = (float)currentDoc.Y - h / 2;
        }

        vm.InsertShapeAt(vm.Toolbar.PendingShapeType, x, y, w, h);
    }

    private void ClearPredictedInk()
    { using (_predictedVisual.RenderOpen()) { } }

    // ══════════════════════════════════════════════════════════════════════════
    // 通道 A：单指触摸绘图
    // 不使用 StylusDevice.Capture（与 ManipulationSystem 互斥，会导致递归崩溃）
    // ══════════════════════════════════════════════════════════════════════════

    protected override void OnTouchDown(TouchEventArgs e)
    {
        base.OnTouchDown(e);
        _activeTouchCount++;

        if (_activeTouchCount >= 2)
        {
            // 第二根手指：中止当前笔划，让 Manipulation 接管
            AbortCurrentStroke();
            return;
        }

        // 第一根手指
        e.TouchDevice.Capture(this); // TouchCapture 不影响 ManipulationSystem
        e.Handled = true;

        var vm = ViewModel;
        if (vm is null) return;
        Focus();
        _primaryTouchId = e.TouchDevice.Id;

        var screen = e.GetTouchPoint(this).Position;
        var doc = ToDoc(screen);

        if (vm.Toolbar.CurrentTool == ToolType.Hand)
        {
            _isPanning = true;
            _panStartScreen = screen;
            _panStartMatrix = _viewMatrix;
        }
        else if (vm.Toolbar.CurrentTool == ToolType.Select)
        {
            _controller?.OnDown(screen);
        }
        else if (vm.Toolbar.CurrentTool == ToolType.Shape)
        {
            _shapeStartDoc = doc;
        }
        else if (vm.Toolbar.CurrentTool is ToolType.Pen or ToolType.Highlighter or ToolType.Marker)
        {
            _currentPoints = [new PressurePoint((float)doc.X, (float)doc.Y, 0.5f, 0)];
        }
    }

    protected override void OnTouchMove(TouchEventArgs e)
    {
        base.OnTouchMove(e);
        if (_isManipulating || e.TouchDevice.Id != _primaryTouchId) return;
        e.Handled = true;

        var vm = ViewModel;
        if (vm is null) return;
        var screen = e.GetTouchPoint(this).Position;
        var doc = ToDoc(screen);

        if (_isPanning)
        {
            var dx = screen.X - _panStartScreen.X;
            var dy = screen.Y - _panStartScreen.Y;
            _viewMatrix = _panStartMatrix;
            _viewMatrix.Translate(dx, dy);
            _viewMatrixTransform.Matrix = _viewMatrix;
            _controller?.RenderOverlay();
            return;
        }

        if (vm.Toolbar.CurrentTool == ToolType.Shape)
        {
            if (_shapeStartDoc is not null) RenderShapePreview(doc);
            return;
        }

        if (vm.Toolbar.CurrentTool == ToolType.Select)
        {
            _controller?.OnMove(screen);
            return;
        }

        if (_currentPoints is not null &&
            vm.Toolbar.CurrentTool is ToolType.Pen or ToolType.Highlighter or ToolType.Marker)
        {
            _lastTouchTimestampMs = Environment.TickCount64;
            _isDrawing = true;
            _currentPoints.Add(new PressurePoint((float)doc.X, (float)doc.Y, 0.5f, Environment.TickCount64));
            RenderWetInk();
        }
        else if (vm.Toolbar.CurrentTool == ToolType.Eraser)
        {
            _ = vm.EraseAtAsync((float)doc.X, (float)doc.Y);
        }
    }

    protected override async void OnTouchUp(TouchEventArgs e)
    {
        base.OnTouchUp(e);
        // async void 事件处理器：未捕获异常会直接升级为应用级未处理异常导致崩溃，
        // 而下方 await 链（OnUpAsync/CommitResizeAsync 等含 Matrix.Invert）可能抛出，必须兜底。
        try
        {
            _activeTouchCount = Math.Max(0, _activeTouchCount - 1);

            if (e.TouchDevice.Id != _primaryTouchId)
            {
                e.TouchDevice.Capture(null);
                return;
            }

            var screen = e.GetTouchPoint(this).Position;
            e.TouchDevice.Capture(null);
            e.Handled = true;
            _primaryTouchId = -1;
            _isPanning = false;

            var vm = ViewModel;
            if (vm is null) return;

            if (vm.Toolbar.CurrentTool == ToolType.Select && _controller is not null)
            {
                await _controller.OnUpAsync(screen);
                vm.NotifySelectionChanged();
                return;
            }

            if (vm.Toolbar.CurrentTool == ToolType.Shape)
            {
                CommitShape(ToDoc(screen));
                return;
            }

            await CommitOrClearAsync(vm);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OnTouchUp] {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 通道 B：双指 Manipulation（平移 + 捏合缩放）
    // ══════════════════════════════════════════════════════════════════════════

    protected override void OnManipulationStarting(ManipulationStartingEventArgs e)
    {
        base.OnManipulationStarting(e);
        // 单指不进入 Manipulation（否则 Translate 手势会立刻阻断绘图）
        if (_activeTouchCount < 2)
        {
            e.Cancel();
            return;
        }
        e.ManipulationContainer = this;
        e.Mode = ManipulationModes.Translate | ManipulationModes.Scale;
        e.Handled = true;
    }

    protected override void OnManipulationStarted(ManipulationStartedEventArgs e)
    {
        base.OnManipulationStarted(e);
        _isManipulating = true;
        AbortCurrentStroke();
        e.Handled = true;
    }

    // 视图缩放上下限，防止接近奇异矩阵（Invert 抛异常）或坐标数值爆炸
    private const double MinViewScale = 0.1;
    private const double MaxViewScale = 10.0;

    /// <summary>以 pivot 为中心缩放视图，并将累计缩放钳制在 [MinViewScale, MaxViewScale]。</summary>
    private void ApplyZoomClamped(double factor, Point pivot)
    {
        double current = _viewMatrix.M11;   // 视图矩阵仅含缩放+平移（无旋转），M11 即当前缩放
        if (current <= 1e-9) current = 1;
        double target = current * factor;
        if (target < MinViewScale) factor = MinViewScale / current;
        else if (target > MaxViewScale) factor = MaxViewScale / current;
        if (Math.Abs(factor - 1.0) < 1e-6) return;
        _viewMatrix.ScaleAtPrepend(factor, factor, pivot.X, pivot.Y);
    }

    protected override void OnManipulationDelta(ManipulationDeltaEventArgs e)
    {
        base.OnManipulationDelta(e);
        var d = e.DeltaManipulation;
        var origin = e.ManipulationOrigin;

        _viewMatrix.Translate(d.Translation.X, d.Translation.Y);
        var s = d.Scale.X;
        if (Math.Abs(s - 1.0) > 0.002)
            ApplyZoomClamped(s, origin);

        _viewMatrixTransform.Matrix = _viewMatrix;
        _controller?.RenderOverlay();
        e.Handled = true;
    }

    protected override void OnManipulationCompleted(ManipulationCompletedEventArgs e)
    {
        base.OnManipulationCompleted(e);
        _isManipulating = false;
        _activeTouchCount = 0;
        e.Handled = true;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 通道 C：纯鼠标（非触控设备）
    // ══════════════════════════════════════════════════════════════════════════

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        // 触控设备走通道 A/B；此处只处理真正的鼠标
        if (e.StylusDevice is not null) return;
        // 工具栏按钮点击不触发画布操作
        if (e.OriginalSource is Button) return;

        var vm = ViewModel;
        if (vm is null) return;
        Focus();
        CaptureMouse();

        var screen = e.GetPosition(this);
        var doc = ToDoc(screen);

        if (vm.Toolbar.CurrentTool == ToolType.Hand)
        {
            _isPanning = true;
            _panStartScreen = screen;
            _panStartMatrix = _viewMatrix;
            return;
        }
        if (vm.Toolbar.CurrentTool == ToolType.Select)
        {
            _controller?.OnDown(screen);
            return;
        }
        if (vm.Toolbar.CurrentTool == ToolType.Shape)
        {
            _shapeStartDoc = doc;
            return;
        }
        if (vm.Toolbar.CurrentTool is ToolType.Pen or ToolType.Highlighter or ToolType.Marker)
            _currentPoints = [new PressurePoint((float)doc.X, (float)doc.Y, 0.5f, 0)];
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.StylusDevice is not null) return;
        var vm = ViewModel;
        if (vm is null || e.LeftButton != MouseButtonState.Pressed) return;

        var screen = e.GetPosition(this);
        var doc = ToDoc(screen);

        if (_isPanning)
        {
            var cur = screen;
            _viewMatrix = _panStartMatrix;
            _viewMatrix.Translate(cur.X - _panStartScreen.X, cur.Y - _panStartScreen.Y);
            _viewMatrixTransform.Matrix = _viewMatrix;
            _controller?.RenderOverlay();
            return;
        }

        if (vm.Toolbar.CurrentTool == ToolType.Shape)
        {
            if (_shapeStartDoc is not null) RenderShapePreview(doc);
            return;
        }

        if (vm.Toolbar.CurrentTool == ToolType.Select)
        {
            _controller?.OnMove(screen);
            return;
        }

        if (_currentPoints is not null &&
            vm.Toolbar.CurrentTool is ToolType.Pen or ToolType.Highlighter or ToolType.Marker)
        {
            _lastTouchTimestampMs = Environment.TickCount64;
            _isDrawing = true;
            _currentPoints.Add(new PressurePoint((float)doc.X, (float)doc.Y, 0.5f, Environment.TickCount64));
            RenderWetInk();
        }
        else if (vm.Toolbar.CurrentTool == ToolType.Eraser)
        {
            _ = vm.EraseAtAsync((float)doc.X, (float)doc.Y);
        }
    }

    protected override async void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (e.StylusDevice is not null) return;
        // async void：异常须兜底，避免下方 await 链抛出导致应用崩溃（参见 OnTouchUp）。
        try
        {
            ReleaseMouseCapture();
            _isPanning = false;
            var vm = ViewModel;
            if (vm is null) return;

            if (vm.Toolbar.CurrentTool == ToolType.Select && _controller is not null)
            {
                var screen = e.GetPosition(this);
                await _controller.OnUpAsync(screen);
                vm.NotifySelectionChanged();
                return;
            }

            if (vm.Toolbar.CurrentTool == ToolType.Shape)
            {
                CommitShape(ToDoc(e.GetPosition(this)));
                return;
            }

            await CommitOrClearAsync(vm);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OnMouseLeftButtonUp] {ex.Message}");
        }
    }

    // ── Zoom (Ctrl + Scroll) ──────────────────────────────────────────────────

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl)) return;
        var pivot = e.GetPosition(this);
        var factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        ApplyZoomClamped(factor, pivot);
        _viewMatrixTransform.Matrix = _viewMatrix;
        _controller?.RenderOverlay();
        e.Handled = true;
    }

    // ── Keyboard shortcuts ────────────────────────────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        { ViewModel?.UndoCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
        { ViewModel?.RedoCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.D0 && Keyboard.Modifiers == ModifierKeys.Control)
        { ResetView(); e.Handled = true; }
        else if (e.Key == Key.F12 && Keyboard.Modifiers == ModifierKeys.None)
        { ShowLatency = !ShowLatency; e.Handled = true; }
        else if (e.Key == Key.Delete && ViewModel is { } vm && vm.HasSelection)
        { _ = vm.DeleteSelectedAsync(); e.Handled = true; }
        else if (e.Key == Key.Escape && ViewModel is { } vmEsc && vmEsc.HasSelection)
        { _ = vmEsc.ClearSelectionAsync(); e.Handled = true; }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control
                 && ViewModel is { } vmCopy && vmCopy.HasSelection)
        { _ = vmCopy.CopySelectedAsync(); e.Handled = true; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void AbortCurrentStroke()
    {
        _isDrawing = false;
        _prevPredicted = null;
        _currentPoints = null;
        _isPanning = false;
        ClearWetInk();
        ClearPredictedInk();
    }

    private async Task CommitOrClearAsync(WhiteboardViewModel vm)
    {
        _isDrawing = false;
        _prevPredicted = null;
        if (_currentPoints is { Count: >= 2 })
        {
            var pts = _currentPoints;
            _currentPoints = null;
            ClearPredictedInk();                // 预测层立即清除
            await vm.CommitStrokeAsync(pts);    // 等 Executor 真正写入页面后再继续
            ClearWetInk();                      // 提交层已就绪，此时清湿墨迹无空白帧
        }
        else
        {
            _currentPoints = null;
            ClearWetInk();
            ClearPredictedInk();
        }
    }

    private Point ToDoc(Point screen)
    {
        var inv = _viewMatrix;
        if (!inv.IsIdentity) inv.Invert();
        return inv.Transform(screen);
    }

    private void ResetView()
    {
        _viewMatrix = Matrix.Identity;
        _viewMatrixTransform.Matrix = _viewMatrix;
        _controller?.RenderOverlay();
    }

    protected override void OnRender(DrawingContext drawingContext)
        => drawingContext.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
}

/// <summary>
/// 滑动窗口延迟统计：记录最近 N 个样本，提供均值和 P95。
/// </summary>
internal sealed class LatencyStats
{
    private const int WindowSize = 120;
    private readonly double[] _buf = new double[WindowSize];
    private int _head;
    private int _filled;

    public int Count => _filled;
    public double Avg { get; private set; }
    public double P95 { get; private set; }

    public void Record(double ms)
    {
        _buf[_head] = ms;
        _head = (_head + 1) % WindowSize;
        if (_filled < WindowSize) _filled++;

        // 均值
        double sum = 0;
        for (int i = 0; i < _filled; i++) sum += _buf[i];
        Avg = sum / _filled;

        // P95（排序拷贝）
        var sorted = _buf[.._filled].OrderBy(x => x).ToArray();
        int p95Idx = (int)Math.Ceiling(_filled * 0.95) - 1;
        P95 = sorted[Math.Max(0, p95Idx)];
    }
}