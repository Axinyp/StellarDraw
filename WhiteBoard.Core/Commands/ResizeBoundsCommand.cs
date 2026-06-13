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
        _page      = page;
        _snapshots = objects
            .Select(o => new Snapshot(o, o.BoundsX, o.BoundsY, o.BoundsWidth, o.BoundsHeight,
                                      (float[])o.Transform.Clone()))
            .ToList();
        _newBounds = newBounds;
    }

    public IReadOnlyList<Guid> AffectedObjectIds => _snapshots.Select(s => s.Obj.Id).ToList();
    public bool SupportsObjectUndo(Guid id)           => false;
    public IWhiteboardCommand? CreateObjectUndo(Guid id) => null;

    public void Execute()
    {
        for (int i = 0; i < _snapshots.Count; i++)
        {
            var snap = _snapshots[i];
            var (nx, ny, nw, nh) = _newBounds[i];
            _page.SpatialIndex.Update(snap.Obj, () => ApplyResize(snap, nx, ny, nw, nh));
        }
    }

    public void Undo()
    {
        foreach (var snap in _snapshots)
            _page.SpatialIndex.Update(snap.Obj, () =>
                snap.Obj.SetBoundsAndTransform(
                    snap.OldX, snap.OldY, snap.OldW, snap.OldH, snap.OldTransform));
    }

    // ── 缩放计算 ─────────────────────────────────────────────────────────────

    private static void ApplyResize(Snapshot snap, float nx, float ny, float nw, float nh)
    {
        var obj = snap.Obj;

        if (obj is StrokeObject or PathObject)
        {
            // 渲染器使用完整 Matrix3x2：docX = M11 * naturalX + OffsetX
            // 计算新矩阵，使自然坐标系（Points / PathData）映射到新 Bounds
            float oldM11 = snap.OldTransform[0];
            float oldM22 = snap.OldTransform[3];
            float oldTx  = snap.OldTransform[4];
            float oldTy  = snap.OldTransform[5];

            // 缩放比例（基于旧 Bounds）
            float sx = snap.OldW > 0 ? nw / snap.OldW : 1f;
            float sy = snap.OldH > 0 ? nh / snap.OldH : 1f;

            float newM11 = oldM11 * sx;
            float newM22 = oldM22 * sy;

            // 自然坐标原点（去掉旧平移后的最小坐标）
            float natX = oldM11 != 0f ? (snap.OldX - oldTx) / oldM11 : snap.OldX;
            float natY = oldM22 != 0f ? (snap.OldY - oldTy) / oldM22 : snap.OldY;

            // 新偏移：使 natX / natY 映射到 nx / ny
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
}
