namespace WhiteBoard.Core.Domain;

/// <summary>
/// 当前页面的选中对象集合，持有于 WhiteboardSession 中。
/// 所有读写均在 Session 执行器线程，无需额外同步。
/// AI 工具（select_objects、move_selected 等）通过 IWhiteboardAiActions 间接操作此集合。
/// </summary>
public sealed class SelectionModel
{
    private readonly HashSet<Guid> _selected = new();

    public IReadOnlySet<Guid> Selected => _selected;
    public bool IsEmpty => _selected.Count == 0;
    public int  Count   => _selected.Count;

    public void Select(Guid id) => _selected.Add(id);

    public void Deselect(Guid id) => _selected.Remove(id);

    public void SelectAll(IEnumerable<Guid> ids)
    {
        _selected.Clear();
        foreach (var id in ids) _selected.Add(id);
    }

    public bool IsSelected(Guid id) => _selected.Contains(id);

    public void Clear() => _selected.Clear();

    public IReadOnlyList<Guid> ToList() => [.. _selected];
}
