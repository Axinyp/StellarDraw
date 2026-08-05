using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;

namespace WhiteBoard.Core.Commands;

/// <summary>
/// 按比例调整一组对象的 Bounds（用于选择手柄缩放）。
/// 对 StrokeObject / PathObject 同步计算缩放矩阵写入 Transform，
/// 使渲染器通过 MatrixTransform 正确映射原始坐标系到新尺寸。
/// 对 ShapeObject / ImageObject 只更新 Bounds（渲染直接使用 Bounds）。
/// </summary>
public sealed class ResizeBoundsCommand : IWhiteboardCommand
{
    private record Snapshot(
        WhiteboardObject Obj,
        float OldX, float OldY, float OldW, float OldH,
        float[] OldTransform);

    private readonly IReadOnlyList<Snapshot> _snapshots;
    private readonly IReadOnlyList<(float NewX, float NewY, float NewW, float NewH)> _newBounds;
    private readonly PageModel _page;

    public ResizeBoundsCommand(
        IReadOnlyList<WhiteboardObject> objects,
        IReadOnlyList<(float x, float y, float w, float h)> newBounds,
        PageModel page)
    {
        _page = page;
        _snapshots = objects
            .Select(o => new Snapshot(o, o.BoundsX, o.BoundsY, o.BoundsWidth, o.BoundsHeight,
                                      (float[])o.Transform.Clone()))
            .ToList();
        _newBounds = newBounds;
    }

    public IReadOnlyList<Guid> AffectedObjectIds => _snapshots.Select(s => s.Obj.Id).ToList();

    public bool SupportsObjectUndo(Guid id) => false;

    public IWhiteboardCommand? CreateObjectUndo(Guid id) => null;

    public void Execute()
    {
        for (int i = 0; i < _snapshots.Count; i++)
        {
            var snap = _snapshots[i];
            var (nx, ny, nw, nh) = _newBounds[i];
            _page.SpatialIndex.Update(snap.Obj, () => ApplyResize(snap, nx, ny, nw, nh));
        }
        _page.MarkContentChanged();
    }

    public void Undo()
    {
        foreach (var snap in _snapshots)
            _page.SpatialIndex.Update(snap.Obj, () =>
                snap.Obj.SetBoundsAndTransform(
                    snap.OldX, snap.OldY, snap.OldW, snap.OldH, snap.OldTransform));
        _page.MarkContentChanged();
    }

    // ── 缩放计算 ─────────────────────────────────────────────────────────────

    private static void ApplyResize(Snapshot snap, float nx, float ny, float nw, float nh)
    {
        var obj = snap.Obj;

        // 旧 Transform 无旋转/斜切（M12==M21==0）时，对笔画用「原始点自然范围」精确拟合，
        // 含旋转时回退线性映射（避免破坏旋转，旋转+缩放本就是边缘场景）。
        bool axisAligned = snap.OldTransform[1] == 0f && snap.OldTransform[2] == 0f;

        if (obj is StrokeObject st && axisAligned)
        {
            // 关键修复：基于不变的原始点范围拟合新框，而非含 pad 的旧 Bounds 递推。
            // 旧实现把含笔宽 pad 的 Bounds 当自然范围缩放，pad 随每次缩放被 sx 放大并累积，
            // 导致选中框越来越大于对象（重新选中时尤为明显）。
            float pad = st.Style.Thickness * 0.5f;
            ComputePointsExtent(st, out float mnX, out float mnY, out float mxX, out float mxY);
            float natW = mxX - mnX, natH = mxY - mnY;
            // 新框内缩笔宽，使「渲染点 + 笔宽」恰好填满新 Bounds
            float targetW = MathF.Max(0f, nw - st.Style.Thickness);
            float targetH = MathF.Max(0f, nh - st.Style.Thickness);
            float sx = natW > 1e-4f ? targetW / natW : (snap.OldTransform[0] != 0f ? snap.OldTransform[0] : 1f);
            float sy = natH > 1e-4f ? targetH / natH : (snap.OldTransform[3] != 0f ? snap.OldTransform[3] : 1f);
            float tx = nx + pad - sx * mnX;
            float ty = ny + pad - sy * mnY;
            obj.SetBoundsAndTransform(nx, ny, nw, nh, [sx, 0f, 0f, sy, tx, ty]);
            return;
        }

        if (obj is StrokeObject or PathObject)
        {
            // 线性映射回退（含旋转的笔画 / PathObject）：把旧 Bounds 矩形映射到新 Bounds。
            // PathObject 的 Bounds 为纯几何（无 pad），不存在 pad 累积问题。
            float oldM11 = snap.OldTransform[0];
            float oldM22 = snap.OldTransform[3];
            float oldTx = snap.OldTransform[4];
            float oldTy = snap.OldTransform[5];

            float sx = snap.OldW > 0 ? nw / snap.OldW : 1f;
            float sy = snap.OldH > 0 ? nh / snap.OldH : 1f;

            float newM11 = oldM11 * sx;
            float newM22 = oldM22 * sy;

            float natX = oldM11 != 0f ? (snap.OldX - oldTx) / oldM11 : snap.OldX;
            float natY = oldM22 != 0f ? (snap.OldY - oldTy) / oldM22 : snap.OldY;

            float newTx = nx - newM11 * natX;
            float newTy = ny - newM22 * natY;

            obj.SetBoundsAndTransform(nx, ny, nw, nh,
                [newM11, 0f, 0f, newM22, newTx, newTy]);
        }
        else
        {
            // ShapeObject / ImageObject：渲染直接使用 Bounds，Transform 不变
            obj.SetBounds(nx, ny, nw, nh);
        }
    }

    private static void ComputePointsExtent(StrokeObject st,
        out float minX, out float minY, out float maxX, out float maxY)
    {
        if (st.Points.Count == 0) { minX = minY = maxX = maxY = 0f; return; }
        minX = minY = float.MaxValue; maxX = maxY = float.MinValue;
        foreach (var p in st.Points)
        {
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }
    }
}