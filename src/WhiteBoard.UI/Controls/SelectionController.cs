using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WhiteBoard.UI.Rendering;
using WhiteBoard.UI.ViewModels;

namespace WhiteBoard.UI.Controls;

/// <summary>
/// 选择工具交互控制器：管理橡皮筋圈选、选中框渲染、对象拖移、缩放手柄和旋转手柄。
/// 所有渲染输出至屏幕空间 DrawingVisual（不带视图矩阵变换）。
/// </summary>
public sealed class SelectionController : IDisposable
{
    private enum DragMode
    { None, RubberBand, MoveObjects, ResizeHandle, RotateHandle }

    // ── 尺寸常量 ─────────────────────────────────────────────────────────────
    private const double HandleSize = 8.0;

    private const double HandleHalf = HandleSize / 2;
    private const double HandleHitR = 10.0;
    private const double RotateLineLen = 48.0;   // 旋转虚线长度（从选中框顶边向上）
    private const double RotateRadius = 10.0;   // 旋转圆形手柄半径
    private const double RotateHitR = 14.0;   // 旋转手柄命中半径
    private const double ToolbarGap = 8.0;    // 工具栏与选中框底边的间距

    private readonly DrawingVisual _visual;
    private readonly WhiteboardViewModel _vm;
    private readonly Func<Matrix> _getView;
    private readonly SelectionToolbar _toolbar;
    // 通知 InkSurface 在拖动期间从 committed 层隐藏/恢复被操作对象（方案 B：消除原件与 ghost 重影）
    private readonly Action<IReadOnlyCollection<Guid>?> _setHiddenObjects;

    // 拖拽状态
    private DragMode _mode;

    private Point _downScreen;
    private Point _lastScreen;

    // MoveObjects 状态：屏幕空间累积偏移
    private double _moveDx, _moveDy;

    // ResizeHandle 状态
    private int _activeHandle = -1;

    private Rect _anchorDocBounds;
    private Rect _anchorScreenBounds;
    private List<(Guid Id, Rect DocBounds)>? _resizeSnapshot;

    // RotateHandle 状态
    private Point _rotatePivotScreen;   // 选中框中心（屏幕坐标）

    private Point _rotatePivotDoc;      // 选中框中心（文档坐标）
    private double _rotateStartAngleDeg; // 开始拖拽时的绝对角度
    private Rect _rotateAnchorScreenBounds;

    public SelectionController(
        DrawingVisual visual,
        WhiteboardViewModel vm,
        Func<Matrix> getView,
        Canvas overlayCanvas,
        Action<IReadOnlyCollection<Guid>?> setHiddenObjects)
    {
        _visual = visual;
        _vm = vm;
        _getView = getView;
        _setHiddenObjects = setHiddenObjects;

        _toolbar = new SelectionToolbar();
        overlayCanvas.Children.Add(_toolbar);

        _toolbar.DeleteClicked += OnDeleteClicked;
        _toolbar.CopyClicked += OnCopyClicked;
        _toolbar.BringToFrontClicked += OnBringToFrontClicked;
    }

    // ── 事件入口（由 InkSurface 在 Select 工具模式下调用）───────────────────

    public void OnDown(Point screen)
    {
        _downScreen = _lastScreen = screen;
        var view = _getView();
        var selIds = _vm.Selection.ToList();

        if (selIds.Count > 0)
        {
            var docBounds = GetSelectionDocBounds(selIds);
            if (!docBounds.IsEmpty)
            {
                var screenBounds = DocToScreen(docBounds, view);

                // 先检测缩放手柄（8 个角/边手柄）
                int h = HitTestHandle(screen, screenBounds);
                if (h >= 0)
                {
                    _mode = DragMode.ResizeHandle;
                    _activeHandle = h;
                    _anchorDocBounds = docBounds;
                    _anchorScreenBounds = screenBounds;
                    _resizeSnapshot = selIds
                        .Select(id => _vm.CurrentPage.GetObject(id))
                        .Where(o => o is not null)
                        .Select(o => (o!.Id, new Rect(o.BoundsX, o.BoundsY, o.BoundsWidth, o.BoundsHeight)))
                        .ToList();
                    _setHiddenObjects(selIds);   // 隐藏 committed 原件，缩放预览实时绘对象本体
                    return;
                }

                // 检测旋转手柄（顶边中心上方）
                if (HitTestRotateHandle(screen, screenBounds))
                {
                    _mode = DragMode.RotateHandle;
                    _rotateAnchorScreenBounds = screenBounds;
                    _rotatePivotScreen = new Point(
                        screenBounds.Left + screenBounds.Width / 2,
                        screenBounds.Top + screenBounds.Height / 2);
                    var inv = view; inv.Invert();
                    _rotatePivotDoc = inv.Transform(_rotatePivotScreen);
                    _rotateStartAngleDeg = VectorAngleDeg(screen - _rotatePivotScreen);
                    _setHiddenObjects(selIds);   // 隐藏 committed 原件，旋转预览实时绘对象本体
                    return;
                }

                if (screenBounds.Contains(screen))
                {
                    _mode = DragMode.MoveObjects;
                    _moveDx = _moveDy = 0;
                    _setHiddenObjects(selIds);   // 隐藏 committed 原件，移动时覆盖层画对象本体跟手
                    return;
                }
            }
        }

        // 点击空白区域：开始橡皮筋
        _mode = DragMode.RubberBand;
        _vm.Selection.Clear();
        ClearOverlay();
    }

    public void OnMove(Point screen)
    {
        var dx = screen.X - _lastScreen.X;
        var dy = screen.Y - _lastScreen.Y;
        _lastScreen = screen;

        switch (_mode)
        {
            case DragMode.RubberBand:
                RenderRubberBand(_downScreen, screen);
                break;

            case DragMode.MoveObjects:
                _moveDx += dx;
                _moveDy += dy;
                RenderOverlayWithMoveDelta();
                break;

            case DragMode.ResizeHandle:
                RenderResizePreview(screen);
                break;

            case DragMode.RotateHandle:
                RenderRotatePreview(screen);
                break;
        }
    }

    public async Task OnUpAsync(Point screen)
    {
        var view = _getView();

        switch (_mode)
        {
            case DragMode.RubberBand:
                {
                    var inv = view; inv.Invert();
                    var p1 = inv.Transform(_downScreen);
                    var p2 = inv.Transform(screen);
                    float x = (float)Math.Min(p1.X, p2.X);
                    float y = (float)Math.Min(p1.Y, p2.Y);
                    float w = (float)Math.Abs(p2.X - p1.X);
                    float h = (float)Math.Abs(p2.Y - p1.Y);
                    if (w > 4 && h > 4)
                        await _vm.SelectRegionAsync(x, y, w, h);
                    RenderOverlay();
                    break;
                }

            case DragMode.MoveObjects:
                {
                    var inv = view; inv.Invert();
                    var docDelta = inv.Transform(new Vector(_moveDx, _moveDy));
                    var selIds = _vm.Selection.ToList();
                    if (selIds.Count > 0 && (Math.Abs(_moveDx) > 0.5 || Math.Abs(_moveDy) > 0.5))
                        await _vm.MoveSelectedAsync((float)docDelta.X, (float)docDelta.Y);
                    _moveDx = _moveDy = 0;
                    _setHiddenObjects(null);   // 提交后恢复 committed 显示真实新位置
                    RenderOverlay();
                    break;
                }

            case DragMode.ResizeHandle:
                {
                    await CommitResizeAsync(screen, view);
                    _resizeSnapshot = null;
                    _activeHandle = -1;
                    _setHiddenObjects(null);   // 提交后恢复 committed 显示真实新尺寸
                    RenderOverlay();
                    break;
                }

            case DragMode.RotateHandle:
                {
                    double angleDeg = ComputeRotateAngleDeg(screen);
                    if (Math.Abs(angleDeg) > 0.1)
                        await _vm.RotateSelectedAsync((float)angleDeg,
                            (float)_rotatePivotDoc.X, (float)_rotatePivotDoc.Y);
                    _setHiddenObjects(null);   // 提交后恢复 committed 显示真实新角度
                    RenderOverlay();
                    break;
                }
        }

        _mode = DragMode.None;
    }

    // ── 公共操作 ─────────────────────────────────────────────────────────────

    /// <summary>清除选中状态并擦除覆盖层。</summary>
    public void ClearSelection()
    {
        _vm.Selection.Clear();
        ClearOverlay();
    }

    /// <summary>根据当前选中集合重绘选中框、手柄、旋转柄，并更新浮动工具栏位置。</summary>
    public void RenderOverlay()
    {
        var selIds = _vm.Selection.ToList();
        using var dc = _visual.RenderOpen();

        if (selIds.Count == 0) { _toolbar.Hide(); return; }

        var view = _getView();
        var doc = GetSelectionDocBounds(selIds);
        if (doc.IsEmpty) { _toolbar.Hide(); return; }

        var screen = DocToScreen(doc, view);
        DrawSelectionContent(dc, screen);
        PositionToolbar(screen);
    }

    public void ClearOverlay()
    {
        using (_visual.RenderOpen()) { }
        _toolbar.Hide();
    }

    public void Dispose()
    {
        _toolbar.DeleteClicked -= OnDeleteClicked;
        _toolbar.CopyClicked -= OnCopyClicked;
        _toolbar.BringToFrontClicked -= OnBringToFrontClicked;

        if (_toolbar.Parent is Panel panel)
            panel.Children.Remove(_toolbar);
    }

    private void OnDeleteClicked() => _ = _vm.DeleteSelectedAsync();

    private void OnCopyClicked() => _ = _vm.CopySelectedAsync();

    private void OnBringToFrontClicked() => _ = _vm.BringToFrontSelectedAsync();

    // ── 私有渲染 ─────────────────────────────────────────────────────────────

    private void RenderRubberBand(Point a, Point b)
    {
        using var dc = _visual.RenderOpen();
        var rect = MakeRect(a, b);
        if (rect.Width < 1 || rect.Height < 1) return;
        dc.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(30, 0, 120, 212)),
            new Pen(Brushes.DodgerBlue, 1) { DashStyle = DashStyles.Dash },
            rect);
    }

    private void RenderOverlayWithMoveDelta()
    {
        var selIds = _vm.Selection.ToList();
        using var dc = _visual.RenderOpen();
        if (selIds.Count == 0) return;

        var view = _getView();
        var doc = GetSelectionDocBounds(selIds);
        if (doc.IsEmpty) return;

        // Ghost 渲染：将选中对象绘制在拖拽偏移后的位置
        dc.PushTransform(new MatrixTransform(
            view.M11, view.M12, view.M21, view.M22,
            view.OffsetX + _moveDx, view.OffsetY + _moveDy));
        foreach (var id in selIds)
        {
            var obj = _vm.CurrentPage.GetObject(id);
            if (obj != null) InkRenderer.RenderObject(dc, obj);
        }
        dc.Pop();

        // 选中框和手柄（屏幕空间，带拖拽偏移）；拖拽中工具栏隐藏
        var screen = DocToScreen(doc, view);
        screen.Offset(_moveDx, _moveDy);
        DrawSelectionContent(dc, screen);
        _toolbar.Hide();
    }

    private void RenderResizePreview(Point cursor)
    {
        using var dc = _visual.RenderOpen();
        var selIds = _vm.Selection.ToList();
        var preview = ComputeNewScreenBounds(cursor, _anchorScreenBounds, _activeHandle);

        // 实时绘制对象本体：把 anchor 屏幕框线性映射到 preview 屏幕框，使对象随手柄缩放跟手
        if (_anchorScreenBounds.Width > 0 && _anchorScreenBounds.Height > 0)
        {
            double sx = preview.Width / _anchorScreenBounds.Width;
            double sy = preview.Height / _anchorScreenBounds.Height;
            var m = _getView();   // 文档 → 屏幕
            m.Append(new Matrix(sx, 0, 0, sy,
                preview.X - _anchorScreenBounds.X * sx,
                preview.Y - _anchorScreenBounds.Y * sy));   // 再缩放到 preview 屏幕框
            dc.PushTransform(new MatrixTransform(m));
            foreach (var id in selIds)
            {
                var obj = _vm.CurrentPage.GetObject(id);
                if (obj != null) InkRenderer.RenderObject(dc, obj);
            }
            dc.Pop();
        }

        DrawSelectionContent(dc, preview);
    }

    private void RenderRotatePreview(Point cursor)
    {
        using var dc = _visual.RenderOpen();
        var selIds = _vm.Selection.ToList();
        double angleDeg = ComputeRotateAngleDeg(cursor);

        // 以选中框中心为轴旋转：对象本体与选框一起转动
        // （外层屏幕空间旋转 + 内层 view 变换将文档坐标画到屏幕）
        dc.PushTransform(new RotateTransform(angleDeg, _rotatePivotScreen.X, _rotatePivotScreen.Y));

        dc.PushTransform(new MatrixTransform(_getView()));
        foreach (var id in selIds)
        {
            var obj = _vm.CurrentPage.GetObject(id);
            if (obj != null) InkRenderer.RenderObject(dc, obj);
        }
        dc.Pop();

        DrawSelectionContent(dc, _rotateAnchorScreenBounds);
        dc.Pop();
    }

    // ── 绘制选中框 + 手柄 + 旋转线 ──────────────────────────────────────────

    /// <summary>绘制选中框、8 个缩放手柄、旋转虚线+圆形手柄（顶边中心上方）。</summary>
    private static void DrawSelectionContent(DrawingContext dc, Rect r)
    {
        // 选中框
        dc.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(15, 0, 120, 212)),
            new Pen(new SolidColorBrush(Color.FromRgb(0, 120, 212)), 1.5) { DashStyle = DashStyles.Dash },
            r);

        // 8 个缩放手柄
        var fill = Brushes.White;
        var stroke = new Pen(new SolidColorBrush(Color.FromRgb(0, 120, 212)), 1.5);
        foreach (var pt in GetHandlePoints(r))
            dc.DrawRectangle(fill, stroke,
                new Rect(pt.X - HandleHalf, pt.Y - HandleHalf, HandleSize, HandleSize));

        // 旋转手柄：顶边中心向上延伸虚线 + 圆形（与 ConferenceAIO 一致）
        double cx = r.Left + r.Width / 2;
        var dashPen = new Pen(new SolidColorBrush(Color.FromRgb(0, 120, 212)), 1.5)
        {
            DashStyle = new DashStyle(new double[] { 4, 3 }, 0)
        };
        dc.DrawLine(dashPen,
            new Point(cx, r.Top),
            new Point(cx, r.Top - RotateLineLen));

        var rotatePt = new Point(cx, r.Top - RotateLineLen);
        dc.DrawEllipse(Brushes.White, stroke, rotatePt, RotateRadius, RotateRadius);
        DrawRotateIcon(dc, rotatePt, RotateRadius * 0.55);
    }

    private static void DrawRotateIcon(DrawingContext dc, Point center, double r)
    {
        // 用 4 段短线模拟旋转箭头（十字形+弯曲感）
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0, 120, 212)), 1.5)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        // 顺时针旋转图标：两段圆弧（用 StreamGeometry 近似）
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            // 上半弧（从约 330° 到 210°，顺时针上弧）
            ctx.BeginFigure(new Point(center.X + r * 0.87, center.Y - r * 0.5), false, false);
            ctx.ArcTo(new Point(center.X - r * 0.87, center.Y - r * 0.5),
                new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
            // 箭头头部
            ctx.LineTo(new Point(center.X - r * 0.87 - 2, center.Y - r * 0.5 - 3), true, false);
            ctx.BeginFigure(new Point(center.X - r * 0.87, center.Y - r * 0.5), false, false);
            ctx.LineTo(new Point(center.X - r * 0.87 - 3, center.Y - r * 0.5 + 2), true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    private static void PositionToolbarHelper(SelectionToolbar toolbar, Rect screenBounds)
    {
        double cx = screenBounds.Left + screenBounds.Width / 2;
        // 工具栏在选中框正下方，与旋转手柄（上方）完全分离
        double top = screenBounds.Bottom + ToolbarGap;
        toolbar.ShowAt(cx - toolbar.Width / 2, top);
    }

    // ── 手柄命中检测 ─────────────────────────────────────────────────────────

    // 8 手柄中心点顺序：TL TC TR L R BL BC BR
    private static Point[] GetHandlePoints(Rect r)
    {
        double cx = r.Left + r.Width / 2;
        double cy = r.Top + r.Height / 2;
        return
        [
            new(r.Left,  r.Top),    // 0 TL
            new(cx,      r.Top),    // 1 TC
            new(r.Right, r.Top),    // 2 TR
            new(r.Left,  cy),       // 3 L
            new(r.Right, cy),       // 4 R
            new(r.Left,  r.Bottom), // 5 BL
            new(cx,      r.Bottom), // 6 BC
            new(r.Right, r.Bottom), // 7 BR
        ];
    }

    private static int HitTestHandle(Point screen, Rect selScreen)
    {
        var pts = GetHandlePoints(selScreen);
        for (int i = 0; i < pts.Length; i++)
        {
            var d = screen - pts[i];
            if (d.LengthSquared <= HandleHitR * HandleHitR) return i;
        }
        return -1;
    }

    private static bool HitTestRotateHandle(Point screen, Rect selScreen)
    {
        double cx = selScreen.Left + selScreen.Width / 2;
        // 旋转手柄在顶边上方 RotateLineLen 处
        var handleCenter = new Point(cx, selScreen.Top - RotateLineLen);
        var d = screen - handleCenter;
        return d.LengthSquared <= RotateHitR * RotateHitR;
    }

    // ── 旋转角度计算 ─────────────────────────────────────────────────────────

    private double ComputeRotateAngleDeg(Point screen)
    {
        double currentAngleDeg = VectorAngleDeg(screen - _rotatePivotScreen);
        return currentAngleDeg - _rotateStartAngleDeg;
    }

    private static double VectorAngleDeg(Vector v)
        => Math.Atan2(v.Y, v.X) * (180.0 / Math.PI);

    // ── 坐标转换 ─────────────────────────────────────────────────────────────

    private static Rect DocToScreen(Rect doc, Matrix view)
    {
        var tl = view.Transform(new Point(doc.Left, doc.Top));
        var br = view.Transform(new Point(doc.Right, doc.Bottom));
        return MakeRect(tl, br);
    }

    private static Rect ScreenToDoc(Rect screen, Matrix view)
    {
        var inv = view; inv.Invert();
        var tl = inv.Transform(new Point(screen.Left, screen.Top));
        var br = inv.Transform(new Point(screen.Right, screen.Bottom));
        return MakeRect(tl, br);
    }

    private static Rect MakeRect(Point a, Point b)
        => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
               Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    // ── 选中框边界计算 ───────────────────────────────────────────────────────

    private Rect GetSelectionDocBounds(IReadOnlyList<Guid> ids)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var id in ids)
        {
            var obj = _vm.CurrentPage.GetObject(id);
            if (obj is null) continue;
            minX = Math.Min(minX, obj.BoundsX);
            minY = Math.Min(minY, obj.BoundsY);
            maxX = Math.Max(maxX, obj.BoundsX + obj.BoundsWidth);
            maxY = Math.Max(maxY, obj.BoundsY + obj.BoundsHeight);
        }

        if (minX >= maxX || minY >= maxY) return Rect.Empty;
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    // ── 工具栏定位 ───────────────────────────────────────────────────────────

    private void PositionToolbar(Rect screenBounds)
        => PositionToolbarHelper(_toolbar, screenBounds);

    // ── 缩放手柄拖拽计算 ─────────────────────────────────────────────────────

    private static Rect ComputeNewScreenBounds(Point cursor, Rect anchor, int handle)
    {
        double l = anchor.Left, t = anchor.Top, r = anchor.Right, b = anchor.Bottom;
        return handle switch
        {
            0 => MakeRect(cursor, new Point(r, b)),
            1 => MakeRect(new Point(l, cursor.Y), new Point(r, b)),
            2 => MakeRect(new Point(l, cursor.Y), new Point(cursor.X, b)),
            3 => MakeRect(new Point(cursor.X, t), new Point(r, b)),
            4 => MakeRect(new Point(l, t), new Point(cursor.X, b)),
            5 => MakeRect(cursor, new Point(r, t)),
            6 => MakeRect(new Point(l, t), new Point(r, cursor.Y)),
            7 => MakeRect(new Point(l, t), cursor),
            _ => anchor,
        };
    }

    private async Task CommitResizeAsync(Point cursor, Matrix view)
    {
        if (_resizeSnapshot is null || _resizeSnapshot.Count == 0) return;

        var newScreenBounds = ComputeNewScreenBounds(cursor, _anchorScreenBounds, _activeHandle);
        var newDocBounds = ScreenToDoc(newScreenBounds, view);

        if (newDocBounds.Width < 4 || newDocBounds.Height < 4) return;

        double sx = _anchorDocBounds.Width > 0 ? newDocBounds.Width / _anchorDocBounds.Width : 1;
        double sy = _anchorDocBounds.Height > 0 ? newDocBounds.Height / _anchorDocBounds.Height : 1;

        var updates = _resizeSnapshot
            .Select(snap => (
                snap.Id,
                X: (float)(newDocBounds.X + (snap.DocBounds.X - _anchorDocBounds.X) * sx),
                Y: (float)(newDocBounds.Y + (snap.DocBounds.Y - _anchorDocBounds.Y) * sy),
                // 防御性下限：整体包围盒已限制 >=4，但按比例缩放后单个小对象仍可能退化为 0/负，
                // 钳到 0.5px 以下限避免对象消失或产生非法尺寸。
                W: (float)Math.Max(0.5, snap.DocBounds.Width * sx),
                H: (float)Math.Max(0.5, snap.DocBounds.Height * sy)))
            .ToList<(Guid, float, float, float, float)>();

        await _vm.ResizeBoundsAsync(updates);
    }
}