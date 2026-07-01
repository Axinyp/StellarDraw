using WhiteBoard.Core.Domain.Objects;

namespace WhiteBoard.Core.Domain;

/// <summary>
/// 白板页面模型：维护一组 <see cref="WhiteboardObject"/> 及其空间索引。
/// <para>
/// 对象的增删必须通过 <see cref="AddObject"/> / <see cref="RemoveObject"/> 进行，
/// 以确保 <see cref="SpatialIndex"/> 与内部列表始终同步，并触发 <see cref="ObjectAdded"/>
/// / <see cref="ObjectRemoved"/> 事件（ViewModel 据此请求重绘）。
/// </para>
/// </summary>
public sealed class PageModel
{
    public string Id { get; }
    public string Title { get; set; }
    public string Background { get; set; }
    public long Version => _version;

    private readonly List<WhiteboardObject> _objects = [];
    private readonly SpatialIndex _index = new();
    private long _version;

    public event Action<WhiteboardObject>? ObjectAdded;

    public event Action<WhiteboardObject>? ObjectRemoved;

    public event Action? Cleared;

    public PageModel(string? id = null, string title = "Page", string background = "white")
    {
        Id = id ?? Guid.NewGuid().ToString("N");
        Title = title;
        Background = background;
    }

    public IReadOnlyList<WhiteboardObject> GetAllObjects() => _objects;

    public WhiteboardObject? GetObject(Guid id)
        => _objects.Find(o => o.Id == id);

    internal void AddObject(WhiteboardObject obj)
    {
        _objects.Add(obj);
        _index.Insert(obj);
        _version++;
        ObjectAdded?.Invoke(obj);
    }

    internal void RemoveObject(WhiteboardObject obj)
    {
        _objects.Remove(obj);
        _index.Remove(obj);
        _version++;
        ObjectRemoved?.Invoke(obj);
    }

    internal void Clear()
    {
        var all = _objects.ToList();
        foreach (var obj in all)
        {
            _objects.Remove(obj);
            _index.Remove(obj);
        }
        _version++;
        Cleared?.Invoke();
    }

    internal SpatialIndex SpatialIndex => _index;

    public IReadOnlyList<WhiteboardObject> HitTest(float x, float y, float tolerance = 4f)
        => _index.HitTest(x, y, tolerance);

    public IReadOnlyList<WhiteboardObject> LassoSelect(float left, float top, float right, float bottom)
        => _index.LassoSelect(left, top, right, bottom);
}