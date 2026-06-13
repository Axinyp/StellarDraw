using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;

namespace WhiteBoard.Core.Commands;

/// <summary>按比例调整一组对象的 Bounds（用于选择手柄缩放）。</summary>
public sealed class ResizeBoundsCommand : IWhiteboardCommand
{
    private readonly IReadOnlyList<(WhiteboardObject Obj, float OldX, float OldY, float OldW, float OldH)> _snapshots;
    private readonly IReadOnlyList<(float NewX, float NewY, float NewW, float NewH)> _newBounds;
    private readonly PageModel _page;

    public ResizeBoundsCommand(
        IReadOnlyList<WhiteboardObject> objects,
        IReadOnlyList<(float x, float y, float w, float h)> newBounds,
        PageModel page)
    {
        _page      = page;
        _snapshots = objects.Select(o => (o, o.BoundsX, o.BoundsY, o.BoundsWidth, o.BoundsHeight)).ToList();
        _newBounds = newBounds;
    }

    public IReadOnlyList<Guid> AffectedObjectIds => _snapshots.Select(s => s.Obj.Id).ToList();
    public bool SupportsObjectUndo(Guid id)         => false;
    public IWhiteboardCommand? CreateObjectUndo(Guid id) => null;

    public void Execute()
    {
        for (int i = 0; i < _snapshots.Count; i++)
        {
            var obj = _snapshots[i].Obj;
            var (nx, ny, nw, nh) = _newBounds[i];
            _page.SpatialIndex.Update(obj, () => obj.SetBounds(nx, ny, nw, nh));
        }
    }

    public void Undo()
    {
        foreach (var (obj, ox, oy, ow, oh) in _snapshots)
            _page.SpatialIndex.Update(obj, () => obj.SetBounds(ox, oy, ow, oh));
    }
}
