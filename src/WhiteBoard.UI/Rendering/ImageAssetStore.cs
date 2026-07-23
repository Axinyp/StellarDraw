using System.Windows.Media.Imaging;

namespace WhiteBoard.UI.Rendering;

/// <summary>进程内冻结位图的有界 LRU 缓存。驱逐仅影响显示缓存，不会释放 Core 原始资产。</summary>
public static class ImageAssetStore
{
    /// <summary>可缓存的最大位图条目数。</summary>
    public const int MaxEntryCount = 128;

    /// <summary>按 BGRA32 估算的最大位图缓存字节数。</summary>
    public const long MaxEstimatedBytes = 128L * 1024 * 1024;

    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, CacheEntry> Entries = new(StringComparer.Ordinal);
    private static readonly LinkedList<string> Lru = [];
    private static long _estimatedBytes;

    /// <summary>缓存冻结位图；超额时优先驱逐最久未使用项。</summary>
    public static void Store(string assetId, BitmapSource bitmap)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentNullException.ThrowIfNull(bitmap);

        var bytes = EstimateBytes(bitmap);
        if (bytes > MaxEstimatedBytes)
            return;

        lock (SyncRoot)
        {
            if (Entries.Remove(assetId, out var existing))
            {
                Lru.Remove(existing.Node);
                _estimatedBytes -= existing.Bytes;
            }

            while (Entries.Count >= MaxEntryCount || _estimatedBytes > MaxEstimatedBytes - bytes)
            {
                var leastRecent = Lru.First;
                if (leastRecent is null)
                    return;

                var evictedId = leastRecent.Value;
                var evicted = Entries[evictedId];
                Entries.Remove(evictedId);
                Lru.RemoveFirst();
                _estimatedBytes -= evicted.Bytes;
            }

            var node = Lru.AddLast(assetId);
            Entries.Add(assetId, new CacheEntry(bitmap, bytes, node));
            _estimatedBytes += bytes;
        }
    }

    /// <summary>获取位图并更新其 LRU 使用顺序。</summary>
    public static BitmapSource? Retrieve(string assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId))
            return null;

        lock (SyncRoot)
        {
            if (!Entries.TryGetValue(assetId, out var entry))
                return null;

            Lru.Remove(entry.Node);
            Lru.AddLast(entry.Node);
            return entry.Bitmap;
        }
    }

    private static long EstimateBytes(BitmapSource bitmap)
    {
        try
        {
            return checked((long)bitmap.PixelWidth * bitmap.PixelHeight * 4);
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    private sealed record CacheEntry(BitmapSource Bitmap, long Bytes, LinkedListNode<string> Node);
}
