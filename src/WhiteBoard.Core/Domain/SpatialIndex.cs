using RBush;
using WhiteBoard.Core.Domain.Objects;

namespace WhiteBoard.Core.Domain;

/// <summary>
/// 基于 RBush R*Tree 的二维空间索引，加速点击检测（HitTest）和框选（LassoSelect）。
/// <para>
/// 索引与 <see cref="PageModel"/> 内部列表必须保持同步：<br/>
/// - 新增/删除对象时通过 <see cref="Insert"/> / <see cref="Remove"/> 更新。<br/>
/// - 几何变更（位置/大小改变）必须通过 <see cref="Update"/> 原子 Remove+mutate+Insert，
///   直接修改对象属性而不调用 Update 会导致索引与实际位置不一致。
/// </para>
/// </summary>
public sealed class SpatialIndex
{
    private readonly RBush<SpatialEntry> _tree = new();

    // 缓存每个对象当前在树中的 entry 实例。
    // 关键：SpatialEntry 是引用类型且未重写 Equals，RBush.Delete 用引用相等匹配；
    // 若 Remove/Update 时重新 new 一个 entry，引用不同则永远删不掉旧条目。
    // 因此必须存下 Insert 时的同一实例，删除时取回它传给 _tree.Delete。
    private readonly Dictionary<WhiteboardObject, SpatialEntry> _entries = new();

    public void Insert(WhiteboardObject obj)
    {
        var entry = MakeEntry(obj);
        _entries[obj] = entry;
        _tree.Insert(entry);
    }

    public void Remove(WhiteboardObject obj)
    {
        // 取回 Insert 时存下的同一 entry 实例，否则引用相等匹配失败、删除无效。
        if (_entries.Remove(obj, out var entry))
            _tree.Delete(entry);
    }

    /// <summary>
    /// 原子 Remove + mutate + Insert。几何变更必须通过此方法，确保索引与对象状态同步。
    /// </summary>
    public void Update(WhiteboardObject obj, Action mutate)
    {
        // 用 mutate 前的旧 entry（其 Envelope 仍是旧位置）删除，RBush 才能正确导航并按引用移除。
        if (_entries.Remove(obj, out var old))
            _tree.Delete(old);
        mutate();
        var entry = MakeEntry(obj);
        _entries[obj] = entry;
        _tree.Insert(entry);
    }

    public IReadOnlyList<WhiteboardObject> HitTest(float x, float y, float tolerance = 4f)
    {
        var results = _tree.Search(new Envelope(x - tolerance, y - tolerance,
                                                x + tolerance, y + tolerance));
        return results.Select(e => e.Object).ToList();
    }

    public IReadOnlyList<WhiteboardObject> LassoSelect(float left, float top, float right, float bottom)
    {
        var results = _tree.Search(new Envelope(left, top, right, bottom));
        return results.Select(e => e.Object).ToList();
    }

    public void Clear()
    {
        _tree.Clear();
        _entries.Clear();
    }

    private static SpatialEntry MakeEntry(WhiteboardObject obj) => new(obj);

    private sealed class SpatialEntry : ISpatialData
    {
        public WhiteboardObject Object { get; }
        private readonly Envelope _envelope;
        public ref readonly Envelope Envelope => ref _envelope;

        public SpatialEntry(WhiteboardObject obj)
        {
            Object = obj;
            _envelope = new Envelope(
                obj.BoundsX,
                obj.BoundsY,
                obj.BoundsX + obj.BoundsWidth,
                obj.BoundsY + obj.BoundsHeight);
        }
    }
}