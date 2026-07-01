using System.Collections.Concurrent;
using System.Windows.Media.Imaging;

namespace WhiteBoard.UI.Rendering;

/// <summary>
/// 进程内图片资产缓存。ImageObject 通过 AssetId 引用，渲染时从此处取 BitmapSource。
/// </summary>
public static class ImageAssetStore
{
    private static readonly ConcurrentDictionary<string, BitmapSource> _store = new();

    public static void Store(string assetId, BitmapSource bitmap)
        => _store[assetId] = bitmap;

    public static BitmapSource? Retrieve(string assetId)
        => _store.TryGetValue(assetId, out var b) ? b : null;
}