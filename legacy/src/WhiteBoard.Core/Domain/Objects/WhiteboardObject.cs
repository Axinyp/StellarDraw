namespace WhiteBoard.Core.Domain.Objects;

/// <summary>
/// 白板对象基类。所有可渲染元素（笔画、图片、文字、思维导图等）均继承此类。
/// <para>
/// 几何约束：<br/>
/// - Bounds（BoundsX/Y/Width/Height）和 Transform（Matrix3x2，6 个 float）不可直接赋值。<br/>
/// - 几何变更必须通过 Command（<see cref="Commands.TransformCommand"/>）调用
///   <see cref="SpatialIndex.Update"/> 原子更新，确保 R*Tree 索引与对象状态一致。<br/>
/// - Transform 存储格式：[M11, M12, M21, M22, OffsetX, OffsetY]（Matrix3x2 行主序）。
/// </para>
/// </summary>
public abstract class WhiteboardObject
{
    public Guid Id { get; } = Guid.NewGuid();
    public int ZIndex { get; set; }
    public bool IsLocked { get; set; }
    public string? OwnerId { get; set; }

    // Bounds and Transform are intentionally not publicly settable.
    // All geometry mutations must go through Command handlers (which also update SpatialIndex).
    public float BoundsX { get; private set; }

    public float BoundsY { get; private set; }
    public float BoundsWidth { get; private set; }
    public float BoundsHeight { get; private set; }

    // 对象创建时的局部坐标（在 Transform 为 identity 时的初始 Bounds）。
    // 平移/旋转时不变，仅在 SetBounds 首次调用（构造）时初始化，resize 不修改。
    // 用于旋转 AABB 计算：world_corner = NewTransform × local_corner，避免 AABB 每次旋转膨胀。
    public float LocalBoundsX { get; private set; }

    public float LocalBoundsY { get; private set; }
    public float LocalBoundsW { get; private set; }
    public float LocalBoundsH { get; private set; }
    private bool _localBoundsInitialized;

    // Matrix3x2 stored as 6 floats: M11,M12,M21,M22,OffsetX,OffsetY
    public float[] Transform { get; private set; } = [1f, 0f, 0f, 1f, 0f, 0f];

    protected WhiteboardObject()
    { }

    protected WhiteboardObject(Guid id, float bx, float by, float bw, float bh, float[] transform)
    {
        Id = id;
        SetBounds(bx, by, bw, bh);
        SetTransform(transform);
    }

    internal void SetBounds(float x, float y, float width, float height)
    {
        if (!WhiteboardGeometry.HasValidBounds(x, y, width, height))
            throw new ArgumentOutOfRangeException(nameof(width), "Bounds 必须是可索引的有限矩形。");

        BoundsX = x; BoundsY = y; BoundsWidth = width; BoundsHeight = height;
        if (!_localBoundsInitialized)
        {
            LocalBoundsX = x; LocalBoundsY = y; LocalBoundsW = width; LocalBoundsH = height;
            _localBoundsInitialized = true;
        }
    }

    internal void SetTransform(float[] m)
    {
        ValidateTransform(m);
        Transform = (float[])m.Clone();
    }

    internal void ApplyTranslation(float dx, float dy)
    {
        var x = BoundsX + dx;
        var y = BoundsY + dy;
        if (!WhiteboardGeometry.HasValidBounds(x, y, BoundsWidth, BoundsHeight) ||
            !float.IsFinite(Transform[4] + dx) || !float.IsFinite(Transform[5] + dy))
            throw new ArgumentOutOfRangeException(nameof(dx), "平移后的几何数据无效。");

        BoundsX = x;
        BoundsY = y;
        Transform[4] += dx;
        Transform[5] += dy;
    }

    internal void SetBoundsAndTransform(float bx, float by, float bw, float bh, float[] transform)
    {
        SetBounds(bx, by, bw, bh);
        SetTransform(transform);
    }

    // 仅更新 World Bounds 和 Transform，不触碰 LocalBounds（旋转专用）。
    internal void SetWorldBoundsAndTransform(float bx, float by, float bw, float bh, float[] transform)
    {
        if (!WhiteboardGeometry.HasValidBounds(bx, by, bw, bh))
            throw new ArgumentOutOfRangeException(nameof(bw), "Bounds 必须是可索引的有限矩形。");

        SetTransform(transform);
        BoundsX = bx; BoundsY = by; BoundsWidth = bw; BoundsHeight = bh;
    }

    private static void ValidateTransform(float[] m)
    {
        if (m is null || m.Length != 6 || m.Any(value => !float.IsFinite(value)))
            throw new ArgumentException("Transform must contain 6 finite floats (Matrix3x2).", nameof(m));
    }
}