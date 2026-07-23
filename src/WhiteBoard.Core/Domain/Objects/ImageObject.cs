namespace WhiteBoard.Core.Domain.Objects;

/// <summary>
/// 图片对象：通过 <see cref="AssetId"/>（资产哈希前缀）引用外部图片资源。
/// OriginalWidth/Height 保留原始分辨率，Bounds 记录画布上的实际显示区域。
/// </summary>
public sealed class ImageObject : WhiteboardObject
{
    /// <summary>SHA256(bytes)[0..16] hex，用于资产去重。</summary>
    public string AssetId { get; private set; }

    internal void SetAssetId(string assetId) => AssetId = assetId;

    public float OriginalWidth { get; }
    public float OriginalHeight { get; }

    public ImageObject(string assetId, float x, float y, float width, float height)
    {
        AssetId = assetId;
        OriginalWidth = width;
        OriginalHeight = height;
        SetBounds(x, y, width, height);
    }

    public ImageObject(Guid id, string assetId, float origW, float origH,
        float bx, float by, float bw, float bh, float[] transform)
        : base(id, bx, by, bw, bh, transform)
    {
        AssetId = assetId;
        OriginalWidth = origW;
        OriginalHeight = origH;
    }
}