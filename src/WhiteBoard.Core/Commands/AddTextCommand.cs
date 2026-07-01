using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;

namespace WhiteBoard.Core.Commands;

/// <summary>将 TextObject 添加到页面的可撤销命令（AI insert_text 工具使用）。</summary>
public sealed class AddTextCommand : IWhiteboardCommand
{
    private readonly TextObject _text;
    private readonly PageModel _page;

    public AddTextCommand(TextObject text, PageModel page)
    {
        _text = text;
        _page = page;
    }

    public IReadOnlyList<Guid> AffectedObjectIds => [_text.Id];

    public bool SupportsObjectUndo(Guid objectId) => objectId == _text.Id;

    public IWhiteboardCommand? CreateObjectUndo(Guid objectId) =>
        objectId == _text.Id ? new DeleteObjectCommand(_text, _page) : null;

    public void Execute() => _page.AddObject(_text);

    public void Undo() => _page.RemoveObject(_text);
}