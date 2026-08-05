using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;

namespace WhiteBoard.Core.Commands;

/// <summary>
/// 旋转一组对象（更新 Bounds + 完整 Transform Matrix3x2）。
/// Undo 还原旋转前的 Bounds 和 Transform 快照。
/// </summary>
public sealed class RotateBoundsCommand : IWhiteboardCommand
{
    private record Snapshot(
        WhiteboardObject Obj,
        float OldX, float OldY, float OldW, float OldH,
        float[] OldTransform);

    private readonly IReadOnlyList<Snapshot> _snapshots;
    private readonly IReadOnlyList<(float X, float Y, float W, float H, float[] Transform)> _newStates;
    private readonly PageModel _page;

    public RotateBoundsCommand(
        IReadOnlyList<WhiteboardObject> objects,
        IReadOnlyList<(float x, float y, float w, float h, float[] transform)> newStates,
        PageModel page)
    {
        _page = page;
        _snapshots = objects
            .Select(o => new Snapshot(o, o.BoundsX, o.BoundsY, o.BoundsWidth, o.BoundsHeight,
                                      (float[])o.Transform.Clone()))
            .ToList();
        _newStates = newStates;
    }

    public IReadOnlyList<Guid> AffectedObjectIds => _snapshots.Select(s => s.Obj.Id).ToList();

    public bool SupportsObjectUndo(Guid id) => false;

    public IWhiteboardCommand? CreateObjectUndo(Guid id) => null;

    public void Execute()
    {
        for (int i = 0; i < _snapshots.Count; i++)
        {
            var snap = _snapshots[i];
            var (nx, ny, nw, nh, nt) = _newStates[i];
            _page.SpatialIndex.Update(snap.Obj, () =>
                snap.Obj.SetWorldBoundsAndTransform(nx, ny, nw, nh, nt));
        }
    }

    public void Undo()
    {
        foreach (var snap in _snapshots)
            _page.SpatialIndex.Update(snap.Obj, () =>
                snap.Obj.SetWorldBoundsAndTransform(
                    snap.OldX, snap.OldY, snap.OldW, snap.OldH, snap.OldTransform));
    }
}