using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;

namespace WhiteBoard.Core.Commands;

/// <summary>
/// 从页面删除对象的可撤销命令。Execute = RemoveObject，Undo = AddObject。
/// 逆命令为 <see cref="AddObjectCommand"/>（通过 <see cref="CreateObjectUndo"/> 返回）。
/// </summary>
public sealed class DeleteObjectCommand : IWhiteboardCommand
{
    private readonly WhiteboardObject _object;
    private readonly PageModel _page;

    public DeleteObjectCommand(WhiteboardObject obj, PageModel page)
    {
        _object = obj;
        _page = page;
    }

    public IReadOnlyList<Guid> AffectedObjectIds => [_object.Id];

    public bool SupportsObjectUndo(Guid objectId) => objectId == _object.Id;

    public IWhiteboardCommand? CreateObjectUndo(Guid objectId) =>
        objectId == _object.Id ? new AddObjectCommand(_object, _page) : null;

    public void Execute() => _page.RemoveObject(_object);

    public void Undo() => _page.AddObject(_object);
}