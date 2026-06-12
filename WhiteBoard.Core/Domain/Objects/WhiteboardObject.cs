using WhiteBoard.Contracts.Services;

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
    public Guid    Id        { get; } = Guid.NewGuid();
    public int     ZIndex    { get; set; }
    public bool    IsLocked  { get; set; }
    public string? OwnerId   { get; set; }

    // Bounds and Transform are intentionally not publicly settable.
    // All geometry mutations must go through Command handlers (which also update SpatialIndex).
    public float BoundsX      { get; private set; }
    public float BoundsY      { get; private set; }
    public float BoundsWidth  { get; private set; }
    public float BoundsHeight { get; private set; }

    // Matrix3x2 stored as 6 floats: M11,M12,M21,M22,OffsetX,OffsetY
    public float[] Transform { get; private set; } = [1f, 0f, 0f, 1f, 0f, 0f];

    protected WhiteboardObject() { }

    protected WhiteboardObject(Guid id, float bx, float by, float bw, float bh, float[] transform)
    {
        Id = id;
        SetBounds(bx, by, bw, bh);
        SetTransform(transform);
    }

    internal void SetBounds(float x, float y, float width, float height)
    {
        BoundsX = x; BoundsY = y; BoundsWidth = width; BoundsHeight = height;
    }

    internal void SetTransform(float[] m)
    {
        if (m.Length != 6) throw new ArgumentException("Transform must be 6 floats (Matrix3x2).", nameof(m));
        Transform = (float[])m.Clone();
    }

    internal void ApplyTranslation(float dx, float dy)
    {
        BoundsX      += dx;
        BoundsY      += dy;
        Transform[4] += dx;
        Transform[5] += dy;
    }
}
