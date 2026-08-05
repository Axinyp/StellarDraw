using System.Security.Cryptography;

namespace WhiteBoard.Core.Services;

/// <summary>
/// 跨层共享的原始图片字节资产仓库（进程内）。仓库以内容哈希去重，并对条目数和总字节数施加硬限制。
/// </summary>
public static class RawImageAssetStore
{
    /// <summary>进程内可缓存的最大原始图片条目数。</summary>
    public const int MaxEntryCount = 1_000;

    /// <summary>进程内可缓存的最大原始图片字节数。</summary>
    public const long MaxTotalBytes = 128L * 1024 * 1024;

    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, byte[]> Assets = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal);
    private static long _totalBytes;

    /// <summary>
    /// 按字节内容计算 AssetId（SHA256 哈希前 16 字节的 hex），用于资产去重。
    /// 长度与 <see cref="PageSerializer"/> 的图片对象二进制格式一致。
    /// </summary>
    public static string ComputeAssetId(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Convert.ToHexString(SHA256.HashData(data).AsSpan(0, 16)).ToLowerInvariant();
    }

    /// <summary>存入已通过格式与资源预算校验的原始图片字节。</summary>
    /// <exception cref="InvalidDataException">图片字节无法安全解码、标识不匹配或超出仓库预算。</exception>
    public static void Store(string assetId, byte[] data)
    {
        if (!TryStore(assetId, data))
            throw new InvalidDataException("图片资产格式或资源预算无效。");
    }

    /// <summary>尝试存入一个原始图片；失败时不会修改仓库。</summary>
    public static bool TryStore(string assetId, byte[]? data)
    {
        if (data is null)
            return false;

        var normalizedAssetId = assetId.ToLowerInvariant();
        return TryStoreBatch(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [normalizedAssetId] = data,
        });
    }

    /// <summary>在不改变仓库的情况下验证一批资产能否整体准入。</summary>
    public static bool CanStoreBatch(IReadOnlyDictionary<string, byte[]> assets)
    {
        if (!TryPrepareBatch(assets, out var prepared))
            return false;

        lock (SyncRoot)
            return HasCapacityFor(prepared);
    }

    /// <summary>原子存入一批原始图片；任何一项无效或超额时均不会写入。</summary>
    public static bool TryStoreBatch(IReadOnlyDictionary<string, byte[]> assets)
    {
        if (!TryPrepareBatch(assets, out var prepared))
            return false;

        lock (SyncRoot)
        {
            if (!HasCapacityFor(prepared))
                return false;

            foreach (var item in prepared)
            {
                if (!Assets.ContainsKey(item.CanonicalId))
                {
                    Assets.Add(item.CanonicalId, item.Data);
                    _totalBytes += item.Data.Length;
                }

                foreach (var alias in item.Aliases)
                    Aliases[alias] = item.CanonicalId;
            }
            return true;
        }
    }

    /// <summary>按规范或历史兼容 AssetId 获取原始字节的防御性副本。</summary>
    public static byte[]? Retrieve(string assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId))
            return null;

        var normalizedAssetId = assetId.ToLowerInvariant();
        lock (SyncRoot)
        {
            if (Aliases.TryGetValue(normalizedAssetId, out var canonicalId) &&
                Assets.TryGetValue(canonicalId, out var data))
                return (byte[])data.Clone();

            if (normalizedAssetId.Length == 32 && normalizedAssetId[16..].All(character => character == '0') &&
                Aliases.TryGetValue(normalizedAssetId[..16], out canonicalId) &&
                Assets.TryGetValue(canonicalId, out data))
                return (byte[])data.Clone();

            return null;
        }
    }

    /// <summary>将规范或历史兼容 AssetId 解析为内容哈希计算出的规范 ID。</summary>
    public static bool TryResolveCanonicalAssetId(string assetId, out string? canonicalAssetId)
    {
        canonicalAssetId = null;
        if (string.IsNullOrWhiteSpace(assetId))
            return false;

        var normalizedAssetId = assetId.ToLowerInvariant();
        lock (SyncRoot)
        {
            if (Aliases.TryGetValue(normalizedAssetId, out var canonicalId) && Assets.ContainsKey(canonicalId))
            {
                canonicalAssetId = canonicalId;
                return true;
            }

            if (normalizedAssetId.Length == 32 && normalizedAssetId[16..].All(character => character == '0') &&
                Aliases.TryGetValue(normalizedAssetId[..16], out canonicalId) && Assets.ContainsKey(canonicalId))
            {
                canonicalAssetId = canonicalId;
                return true;
            }

            return false;
        }
    }

    private static bool TryPrepareBatch(
        IReadOnlyDictionary<string, byte[]>? assets,
        out IReadOnlyList<PreparedAsset> prepared)
    {
        prepared = [];
        if (assets is null || assets.Count > MaxEntryCount)
            return false;

        var byCanonicalId = new Dictionary<string, PreparedAsset>(StringComparer.Ordinal);
        foreach (var (assetId, data) in assets)
        {
            if (!IsSupportedAssetId(assetId) || data is null || !ImageAssetValidator.TryValidate(data, out _))
                return false;

            var normalizedAssetId = assetId.ToLowerInvariant();
            var canonicalId = ComputeAssetId(data);
            if (!MatchesAssetId(normalizedAssetId, canonicalId))
                return false;

            if (!byCanonicalId.TryGetValue(canonicalId, out var existing))
            {
                var aliases = new HashSet<string>(StringComparer.Ordinal) { canonicalId, normalizedAssetId };
                if (normalizedAssetId.Length == 16)
                    aliases.Add(normalizedAssetId.PadRight(32, '0'));
                byCanonicalId.Add(canonicalId, new PreparedAsset(canonicalId, (byte[])data.Clone(), aliases));
                continue;
            }

            if (!existing.Data.AsSpan().SequenceEqual(data))
                return false;
            existing.Aliases.Add(normalizedAssetId);
            if (normalizedAssetId.Length == 16)
                existing.Aliases.Add(normalizedAssetId.PadRight(32, '0'));
        }

        prepared = byCanonicalId.Values.ToArray();
        return true;
    }

    private static bool HasCapacityFor(IReadOnlyList<PreparedAsset> prepared)
    {
        long newBytes = 0;
        var newEntries = 0;
        foreach (var item in prepared)
        {
            if (Assets.ContainsKey(item.CanonicalId))
                continue;
            newEntries++;
            if (newBytes > MaxTotalBytes - item.Data.Length)
                return false;
            newBytes += item.Data.Length;
        }

        return Assets.Count <= MaxEntryCount - newEntries && _totalBytes <= MaxTotalBytes - newBytes;
    }

    private static bool IsSupportedAssetId(string? assetId) =>
        assetId is not null && assetId.Length is 16 or 32 && assetId.All(Uri.IsHexDigit);

    private static bool MatchesAssetId(string assetId, string canonicalId) =>
        assetId.Length == 32
            ? string.Equals(assetId, canonicalId, StringComparison.OrdinalIgnoreCase)
            : string.Equals(assetId, canonicalId[..16], StringComparison.OrdinalIgnoreCase);

    private sealed class PreparedAsset(string canonicalId, byte[] data, HashSet<string> aliases)
    {
        public string CanonicalId { get; } = canonicalId;
        public byte[] Data { get; } = data;
        public HashSet<string> Aliases { get; } = aliases;
    }
}
