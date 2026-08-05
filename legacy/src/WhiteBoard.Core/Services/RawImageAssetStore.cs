using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace WhiteBoard.Core.Services;

/// <summary>
/// 跨层共享的原始图片字节资产仓库（进程内）。
/// <para>
/// AI 层生成/插入图片时把 PNG 字节按 <c>AssetId</c> 存入此处；UI 渲染层在 WPF
/// BitmapSource 缓存未命中时回退到这里取字节解码。这是 AI 层与 UI 层之间唯一的
/// 公共底座——两者都依赖 Core，但 AI 不依赖 UI，故图片字节必须经由 Core 中转。
/// </para>
/// </summary>
public static class RawImageAssetStore
{
    private static readonly ConcurrentDictionary<string, byte[]> _store = new();

    /// <summary>
    /// 按字节内容计算 AssetId（SHA256 哈希前 8 字节的 hex），用于资产去重。
    /// 与 UI 手动插图所用算法保持一致，确保相同图片得到相同 AssetId。
    /// </summary>
    public static string ComputeAssetId(byte[] data)
        => Convert.ToHexString(SHA256.HashData(data).AsSpan(0, 8)).ToLowerInvariant();

    public static void Store(string assetId, byte[] data) => _store[assetId] = data;

    public static byte[]? Retrieve(string assetId)
        => _store.TryGetValue(assetId, out var d) ? d : null;
}
