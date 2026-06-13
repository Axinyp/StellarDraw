using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WhiteBoard.UI.Rendering;
using WhiteBoard.UI.ViewModels;

namespace WhiteBoard.UI.Controls;

/// <summary>
/// 选择工具交互控制器：管理橡皮筋圈选、选中框渲染、对象拖移和缩放手柄。
/// 所有渲染输出至屏幕空间 DrawingVisual（不带视图矩阵变换）。
/// </summary>
public sealed class SelectionController
{
    private enum DragMode { None, RubberBand, MoveObjects, ResizeHandle }

    private const double HandleSize = 8.0;
    private const double HandleHalf = HandleSize / 2;
    private const double HandleHitR = 10.0;

    private readonly DrawingVisual       _visual;
    private readonly WhiteboardViewModel _vm;
    private readonly Func<Matrix>        _getView;
    private readonly SelectionToolbar    _toolbar;

    // 拖拽状态
    private DragMode _mode;
    private Point    _downScreen;
    private Point    _lastScreen;

    // MoveObjects 状态：屏幕空间累积偏移
    private double _moveDx, _moveDy;

    // ResizeHandle 状态
    private int  _activeHandle = -1;
    private Rect _anchorDocBounds;
    private Rect _anchorScreenBounds;
    private List<(Guid Id, Rect DocBounds)>? _resizeSnapshot;

    public SelectionController(
        DrawingVisual       visual,
        WhiteboardViewModel vm,
        Func<Matrix>        getView,
        Canvas              overlayCanvas)
    {
        _visual  = visual;
        _vm      = vm;
        _getView = getView;

        _toolbar = new SelectionToolbar();
        overlayCanvas.Children.Add(_toolbar);

        _toolbar.DeleteClicked      += () => _ = _vm.DeleteSelectedAsync();
        _toolbar.CopyClicked        += () => _ = _vm.CopySelectedAsync();
        _toolbar.BringToFrontClicked += () => _ = _vm.BringToFrontSelectedAsync();
    }

    // ── 事件入口（由 InkSurface 在 Select 工具模式下调用）───────────────────

    public void OnDown(Point screen)
    {
        _downScreen = _lastScreen = screen;
        var view    = _getView();
        var selIds  = _vm.Selection.ToList();

        if (selIds.Count > 0)
        {
            var docBounds = GetSelectionDocBounds(selIds);
            if (!docBounds.IsEmpty)
            {
                var screenBounds = DocToScreen(docBounds, view);

                int h = HitTestHandle(screen, screenBounds);
                if (h >= 0)
                {
                    _mode                = DragMode.ResizeHandle;
                    _activeHandle        = h;
                    _anchorDocBounds     = docBounds;
                    _anchorScreenBounds  = screenBounds;
                    _resizeSnapshot      = selIds
                        .Select(id => _vm.CurrentPage.GetObject(id))
                        .Where(o => o is not null)
                        .Select(o => (o!.Id, new Rect(o.BoundsX, o.BoundsY, o.BoundsWidth, o.BoundsHeight)))
                        .ToList();
                    return;
                }

                if (screenBounds.Contains(screen))
                {
                    _mode   = DragMode.MoveObjects;
                    _moveDx = _moveDy = 0;
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
                var p1  = inv.Transform(_downScreen);
                var p2  = inv.Transform(screen);
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
                var inv      = view; inv.Invert();
                var docDelta = inv.Transform(new Vector(_moveDx, _moveDy));
                var selIds   = _vm.Selection.ToList();
                if (selIds.Count > 0 && (Math.Abs(_moveDx) > 0.5 || Math.Abs(_moveDy) > 0.5))
                    await _vm.MoveSelectedAsync((float)docDelta.X, (float)docDelta.Y);
                _moveDx = _moveDy = 0;
                RenderOverlay();
                break;
            }

            case DragMode.ResizeHandle:
            {
                await CommitResizeAsync(screen, view);
                _resizeSnapshot = null;
                _activeHandle   = -1;
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

    /// <summary>根据当前选中集合重绘选中框和手柄，并更新浮动工具栏位置。</summary>
    public void RenderOverlay()
    {
        var selIds = _vm.Selection.ToList();
        using var dc = _visual.RenderOpen();

        if (selIds.Count == 0)
        {
            _toolbar.Hide();
            return;
        }

        var view   = _getView();
        var doc    = GetSelectionDocBounds(selIds);
        if (doc.IsEmpty) { _toolbar.Hide(); return; }

        var screen = DocToScreen(doc, view);
        DrawSelectionRect(dc, screen);
        DrawHandles(dc, screen);

        // 工具栏显示在选中框右上角偏移处
        _toolbar.ShowAt(screen.Right - _toolbar.Width, screen.Top - _toolbar.Height - 6);
    }

    public void ClearOverlay()
    {
        using (_visual.RenderOpen()) { }
        _toolbar.Hide();
    }

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

        var view   = _getView();
        var doc    = GetSelectionDocBounds(selIds);
        if (doc.IsEmpty) return;

        // Ghost 渲染：将选中对象绘制在拖拽偏移后的位置
        // 矩阵 = viewMatrix 附加屏幕空间平移（view.OffsetX + moveDx，而非 Matrix.Translate）
        dc.PushTransform(new MatrixTransform(
            view.M11, view.M12, view.M21, view.M22,
            view.OffsetX + _moveDx, view.OffsetY + _moveDy));
        foreach (var id in selIds)
        {
            var obj = _vm.CurrentPage.GetObject(id);
            if (obj != null) InkRenderer.RenderObject(dc, obj);
        }
        dc.Pop();

        // 选中框和手柄（屏幕空间，带拖拽偏移）
        var screen = DocToScreen(doc, view);
        screen.Offset(_moveDx, _moveDy);
        DrawSelectionRect(dc, screen);
        DrawHandles(dc, screen);
    }

    private void RenderResizePreview(Point cursor)
    {
        using var dc      = _visual.RenderOpen();
        var preview = ComputeNewScreenBounds(cursor, _anchorScreenBounds, _activeHandle);
        DrawSelectionRect(dc, preview);
        DrawHandles(dc, preview);
    }

    private static void DrawSelectionRect(DrawingContext dc, Rect r)
    {
        dc.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(15, 0, 120, 212)),
            new Pen(new SolidColorBrush(Color.FromRgb(0, 120, 212)), 1.5) { DashStyle = DashStyles.Dash },
            r);
    }

    private static void DrawHandles(DrawingContext dc, Rect r)
    {
        var fill   = Brushes.White;
        var stroke = new Pen(new SolidColorBrush(Color.FromRgb(0, 120, 212)), 1.5);
        foreach (var pt in GetHandlePoints(r))
            dc.DrawRectangle(fill, stroke,
                new Rect(pt.X - HandleHalf, pt.Y - HandleHalf, HandleSize, HandleSize));
    }

    // 8 手柄中心点顺序：TL TC TR L R BL BC BR
    private static Point[] GetHandlePoints(Rect r)
    {
        double cx = r.Left + r.Width  / 2;
        double cy = r.Top  + r.Height / 2;
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

    // ── 手柄命中检测 ─────────────────────────────────────────────────────────

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

    // ── 坐标转换 ─────────────────────────────────────────────────────────────

    private static Rect DocToScreen(Rect doc, Matrix view)
    {
        var tl = view.Transform(new Point(doc.Left,  doc.Top));
        var br = view.Transform(new Point(doc.Right, doc.Bottom));
        return MakeRect(tl, br);
    }

    private static Rect ScreenToDoc(Rect screen, Matrix view)
    {
        var inv = view; inv.Invert();
        var tl = inv.Transform(new Point(screen.Left,  screen.Top));
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

    // ── 缩放手柄拖拽计算 ─────────────────────────────────────────────────────

    // 根据被拖动的手柄编号和当前鼠标位置，计算新的屏幕空间选中框。
    // 固定角/边：TL→固定BR，TR→固定BL，BL→固定TR，BR→固定TL，
    //            TC→固定底边，BC→固定顶边，L→固定右边，R→固定左边。
    private static Rect ComputeNewScreenBounds(Point cursor, Rect anchor, int handle)
    {
        double l = anchor.Left, t = anchor.Top, r = anchor.Right, b = anchor.Bottom;
        return handle switch
        {
            0 => MakeRect(cursor,              new Point(r, b)),  // TL: BR 固定
            1 => MakeRect(new Point(l, cursor.Y), new Point(r, b)),  // TC: 底边固定
            2 => MakeRect(new Point(l, cursor.Y), new Point(cursor.X, b)),  // TR: BL 固定
            3 => MakeRect(new Point(cursor.X, t), new Point(r, b)),  // L: 右边固定
            4 => MakeRect(new Point(l, t),     new Point(cursor.X, b)),  // R: 左边固定
            5 => MakeRect(cursor,              new Point(r, t)),  // BL: TR 固定
            6 => MakeRect(new Point(l, t),     new Point(r, cursor.Y)),  // BC: 顶边固定
            7 => MakeRect(new Point(l, t),     cursor),          // BR: TL 固定
            _ => anchor,
        };
    }

    private async Task CommitResizeAsync(Point cursor, Matrix view)
    {
        if (_resizeSnapshot is null || _resizeSnapshot.Count == 0) return;

        var newScreenBounds = ComputeNewScreenBounds(cursor, _anchorScreenBounds, _activeHandle);
        var newDocBounds    = ScreenToDoc(newScreenBounds, view);

        if (newDocBounds.Width < 4 || newDocBounds.Height < 4) return; // 防止退化

        double sx = _anchorDocBounds.Width  > 0 ? newDocBounds.Width  / _anchorDocBounds.Width  : 1;
        double sy = _anchorDocBounds.Height > 0 ? newDocBounds.Height / _anchorDocBounds.Height : 1;

        var updates = _resizeSnapshot
            .Select(snap => (
                snap.Id,
                X: (float)(newDocBounds.X + (snap.DocBounds.X - _anchorDocBounds.X) * sx),
                Y: (float)(newDocBounds.Y + (snap.DocBounds.Y - _anchorDocBounds.Y) * sy),
                W: (float)(snap.DocBounds.Width  * sx),
                H: (float)(snap.DocBounds.Height * sy)))
            .ToList<(Guid, float, float, float, float)>();

        await _vm.ResizeBoundsAsync(updates);
    }
}
