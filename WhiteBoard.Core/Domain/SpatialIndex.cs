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

    public void Insert(WhiteboardObject obj)
        => _tree.Insert(MakeEntry(obj));

    public void Remove(WhiteboardObject obj)
        => _tree.Delete(MakeEntry(obj));

    /// <summary>
    /// 原子 Remove + mutate + Insert。几何变更必须通过此方法，确保索引与对象状态同步。
    /// </summary>
    public void Update(WhiteboardObject obj, Action mutate)
    {
        _tree.Delete(MakeEntry(obj));
        mutate();
        _tree.Insert(MakeEntry(obj));
    }

    public IReadOnlyList<WhiteboardObject> HitTest(float x, float y, float tolerance = 4f)
    {
        var results = _tree.Search(new Envelope(x - tolerance, y - tolerance,
                                                x + tolerance, y + tolerance));
        return results.Select(e => e.Object).ToList();
    }

    public IReadOnlyList<WhiteboardObject> LassoSelect(float left, float top, float right, float bottom)
    {
        // 先用 R*Tree 快速缩小候选范围，再用包含检测过滤：
        // 只选中 Bounds 完全在框内的对象，避免框选"空白区"时意外选中边缘对象。
        var results = _tree.Search(new Envelope(left, top, right, bottom));
        return results
            .Where(e => e.Object.BoundsX >= left  && e.Object.BoundsY >= top &&
                        e.Object.BoundsX + e.Object.BoundsWidth  <= right &&
                        e.Object.BoundsY + e.Object.BoundsHeight <= bottom)
            .Select(e => e.Object)
            .ToList();
    }

    public void Clear() => _tree.Clear();

    private static SpatialEntry MakeEntry(WhiteboardObject obj) => new(obj);

    private sealed class SpatialEntry : ISpatialData
    {
        public WhiteboardObject Object { get; }
        private readonly Envelope _envelope;
        public ref readonly Envelope Envelope => ref _envelope;

        public SpatialEntry(WhiteboardObject obj)
        {
            Object    = obj;
            _envelope = new Envelope(
                obj.BoundsX,
                obj.BoundsY,
                obj.BoundsX + obj.BoundsWidth,
                obj.BoundsY + obj.BoundsHeight);
        }
    }
}
