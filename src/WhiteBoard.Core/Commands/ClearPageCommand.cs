using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;

namespace WhiteBoard.Core.Commands;

/// <summary>
/// 清空当前页全部对象的可撤销命令（AI clear_page 工具使用）。
/// Execute 前先快照所有对象；Undo 时原样恢复。
/// </summary>
public sealed class ClearPageCommand : IWhiteboardCommand
{
    private readonly PageModel _page;
    private IReadOnlyList<WhiteboardObject> _snapshot = [];

    public ClearPageCommand(PageModel page) => _page = page;

    public IReadOnlyList<Guid> AffectedObjectIds =>
        _snapshot.Select(o => o.Id).ToList();

    // 清空命令影响全部对象，不支持单对象粒度撤销
    public bool SupportsObjectUndo(Guid _) => false;

    public IWhiteboardCommand? CreateObjectUndo(Guid _) => null;

    public void Execute()
    {
        _snapshot = _page.GetAllObjects().ToList();
        _page.Clear();
    }

    public void Undo()
    {
        foreach (var obj in _snapshot)
            _page.AddObject(obj);
    }
}