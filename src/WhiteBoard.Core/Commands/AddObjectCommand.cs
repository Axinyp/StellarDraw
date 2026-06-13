using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;

namespace WhiteBoard.Core.Commands;

/// <summary>
/// 将对象添加到页面的可撤销命令。Execute = AddObject，Undo = RemoveObject。
/// 逆命令为 <see cref="DeleteObjectCommand"/>（通过 <see cref="CreateObjectUndo"/> 返回）。
/// </summary>
public sealed class AddObjectCommand : IWhiteboardCommand
{
    private readonly WhiteboardObject _object;
    private readonly PageModel        _page;

    public AddObjectCommand(WhiteboardObject obj, PageModel page)
    {
        _object = obj;
        _page   = page;
    }

    public IReadOnlyList<Guid> AffectedObjectIds => [_object.Id];

    public bool SupportsObjectUndo(Guid objectId) => objectId == _object.Id;

    public IWhiteboardCommand? CreateObjectUndo(Guid objectId) =>
        objectId == _object.Id ? new DeleteObjectCommand(_object, _page) : null;

    public void Execute() => _page.AddObject(_object);
    public void Undo()    => _page.RemoveObject(_object);
}
