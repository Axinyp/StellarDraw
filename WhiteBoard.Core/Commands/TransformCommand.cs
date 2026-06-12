using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;

namespace WhiteBoard.Core.Commands;

/// <summary>
/// 对一个或多个对象应用平移变换。
/// 单对象版本 SupportsObjectUndo = true；批量版本 SupportsObjectUndo = false。
/// </summary>
public sealed class TransformCommand : IWhiteboardCommand
{
    private readonly IReadOnlyList<WhiteboardObject> _objects;
    private readonly PageModel                       _page;
    private readonly float                           _dx;
    private readonly float                           _dy;

    public TransformCommand(WhiteboardObject obj, PageModel page, float dx, float dy)
        : this([obj], page, dx, dy) { }

    public TransformCommand(IReadOnlyList<WhiteboardObject> objects, PageModel page, float dx, float dy)
    {
        _objects = objects;
        _page    = page;
        _dx      = dx;
        _dy      = dy;
    }

    public IReadOnlyList<Guid> AffectedObjectIds => _objects.Select(o => o.Id).ToList();

    public bool SupportsObjectUndo(Guid objectId) =>
        _objects.Count == 1 && _objects[0].Id == objectId;

    public IWhiteboardCommand? CreateObjectUndo(Guid objectId) =>
        SupportsObjectUndo(objectId)
            ? new TransformCommand(_objects[0], _page, -_dx, -_dy)
            : null;

    public void Execute()
    {
        foreach (var obj in _objects)
            _page.SpatialIndex.Update(obj, () => obj.ApplyTranslation(_dx, _dy));
    }

    public void Undo()
    {
        foreach (var obj in _objects)
            _page.SpatialIndex.Update(obj, () => obj.ApplyTranslation(-_dx, -_dy));
    }
}
