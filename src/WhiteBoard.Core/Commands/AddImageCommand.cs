using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;

namespace WhiteBoard.Core.Commands;

public sealed class AddImageCommand : IWhiteboardCommand
{
    private readonly ImageObject _image;
    private readonly PageModel   _page;

    public AddImageCommand(ImageObject image, PageModel page)
    {
        _image = image;
        _page  = page;
    }

    public IReadOnlyList<Guid> AffectedObjectIds => [_image.Id];

    public bool SupportsObjectUndo(Guid objectId) => objectId == _image.Id;

    public IWhiteboardCommand? CreateObjectUndo(Guid objectId) =>
        objectId == _image.Id ? new DeleteObjectCommand(_image, _page) : null;

    public void Execute() => _page.AddObject(_image);
    public void Undo()    => _page.RemoveObject(_image);
}
