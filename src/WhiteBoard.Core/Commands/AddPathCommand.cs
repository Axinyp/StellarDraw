using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;

namespace WhiteBoard.Core.Commands;

/// <summary>将 PathObject 添加到页面的可撤销命令（AI draw_path 工具使用）。</summary>
public sealed class AddPathCommand : IWhiteboardCommand
{
    private readonly PathObject _path;
    private readonly PageModel  _page;

    public AddPathCommand(PathObject path, PageModel page)
    {
        _path = path;
        _page = page;
    }

    public IReadOnlyList<Guid> AffectedObjectIds => [_path.Id];

    public bool SupportsObjectUndo(Guid objectId) => objectId == _path.Id;

    public IWhiteboardCommand? CreateObjectUndo(Guid objectId) =>
        objectId == _path.Id ? new DeleteObjectCommand(_path, _page) : null;

    public void Execute() => _page.AddObject(_path);
    public void Undo()    => _page.RemoveObject(_path);
}
