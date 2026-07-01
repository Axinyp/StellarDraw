using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;

namespace WhiteBoard.Core.Commands;

/// <summary>将 ShapeObject 添加到页面的可撤销命令（AI insert_shape 工具使用）。</summary>
public sealed class AddShapeCommand : IWhiteboardCommand
{
    private readonly ShapeObject _shape;
    private readonly PageModel _page;

    public AddShapeCommand(ShapeObject shape, PageModel page)
    {
        _shape = shape;
        _page = page;
    }

    public IReadOnlyList<Guid> AffectedObjectIds => [_shape.Id];

    public bool SupportsObjectUndo(Guid objectId) => objectId == _shape.Id;

    public IWhiteboardCommand? CreateObjectUndo(Guid objectId) =>
        objectId == _shape.Id ? new DeleteObjectCommand(_shape, _page) : null;

    public void Execute() => _page.AddObject(_shape);

    public void Undo() => _page.RemoveObject(_shape);
}