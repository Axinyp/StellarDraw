using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;

namespace WhiteBoard.Core.Services;

/// <summary>
/// 读写版本化白板文档包。包内只允许 manifest、page.bin 和按内容哈希命名的图片资产。
/// </summary>
public static class WhiteboardDocumentSerializer
{
    private const int SchemaVersion = 1;
    private const int MaxPageCount = 200;
    private const int MaxAssetCount = 1_000;
    private const int MaxPageBytes = 32 * 1024 * 1024;
    private const int MaxManifestBytes = 256 * 1024;
    private const int MaxAssetBytes = ImageAssetValidator.MaxCompressedBytes;
    private const int MaxPackageBytes = 250 * 1024 * 1024;
    private const int MaxObjectsPerPage = 50_000;
    private const int MaxArchiveEntryCount = MaxPageCount + MaxAssetCount + 1;
    private const float MaxAbsoluteCoordinate = 1_000_000f;
    private const string ManifestEntryName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>将经 Session 捕获的完整文档快照编码为 ZIP 包。</summary>
    public static byte[] Serialize(WhiteboardDocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateDocument(document);

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = new DocumentManifest(
                SchemaVersion,
                document.DocumentId,
                document.CanvasSize.Width,
                document.CanvasSize.Height,
                document.CurrentPageIndex,
                document.Pages.Select(page => new PageManifest(page.Id, page.Title, page.Background)).ToArray());
            WriteEntry(archive, ManifestEntryName, JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));

            foreach (var page in document.Pages)
                WriteEntry(archive, GetPageEntryName(page.Id), page.Data);

            foreach (var (assetId, data) in document.Assets)
                WriteEntry(archive, GetAssetEntryName(assetId), data);
        }

        return output.ToArray();
    }

    /// <summary>完整校验 ZIP 包并构建候选文档；调用方可在成功后原子替换 Session。</summary>
    public static WhiteboardDocumentSnapshot Deserialize(byte[] package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.Length == 0 || package.Length > MaxPackageBytes)
            throw new InvalidDataException("白板文档大小无效。");

        ValidateCentralDirectoryEntryCount(package);
        using var input = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        var entries = ReadEntries(archive);
        if (!entries.TryGetValue(ManifestEntryName, out var manifestData))
            throw new InvalidDataException("白板文档缺少清单。");

        var manifest = DeserializeManifest(manifestData);
        ValidateManifest(manifest);

        var pages = new List<WhiteboardDocumentPageSnapshot>(manifest.Pages.Length);
        var expectedNames = new HashSet<string>(StringComparer.Ordinal) { ManifestEntryName };
        var imageAssetIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var page in manifest.Pages)
        {
            var entryName = GetPageEntryName(page.Id);
            expectedNames.Add(entryName);
            if (!entries.TryGetValue(entryName, out var pageData))
                throw new InvalidDataException($"白板文档缺少页面数据 '{page.Id}'。");

            ValidatePageData(pageData, page);
            var parsedPage = PageSerializer.Deserialize(pageData, page.Id, page.Title);
            foreach (var image in parsedPage.GetAllObjects().OfType<ImageObject>())
                imageAssetIds.Add(image.AssetId);

            pages.Add(new WhiteboardDocumentPageSnapshot(page.Id, page.Title, page.Background, pageData));
        }

        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var assetId in imageAssetIds)
        {
            var entryName = GetAssetEntryName(assetId);
            expectedNames.Add(entryName);
            if (!entries.TryGetValue(entryName, out var assetData))
                throw new InvalidDataException($"白板文档缺少图片资产 '{assetId}'。");
            if (assetData.Length == 0 || assetData.Length > MaxAssetBytes ||
                !ImageAssetValidator.TryValidate(assetData, out _) ||
                !string.Equals(RawImageAssetStore.ComputeAssetId(assetData), assetId, StringComparison.Ordinal))
                throw new InvalidDataException("白板文档包含无效图片资产。");

            assets.Add(assetId, assetData);
        }

        if (!entries.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expectedNames))
            throw new InvalidDataException("白板文档包含未声明的条目。");

        return new WhiteboardDocumentSnapshot(
            manifest.DocumentId,
            new CanvasSizeSnapshot(manifest.CanvasWidth, manifest.CanvasHeight),
            pages,
            manifest.CurrentPageIndex,
            assets);
    }

    /// <summary>仅供安全回归测试构造含指定条目的最小 ZIP 包。</summary>
    public static byte[] CreatePackageForTesting(string entryName, byte[] data)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            WriteEntry(archive, entryName, data);
        return output.ToArray();
    }

    internal static void ValidateDocument(WhiteboardDocumentSnapshot document)
    {
        if (!IsDocumentId(document.DocumentId))
            throw new InvalidDataException("白板文档 ID 无效。");
        if (!float.IsFinite(document.CanvasSize.Width) || document.CanvasSize.Width <= 0 ||
            !float.IsFinite(document.CanvasSize.Height) || document.CanvasSize.Height <= 0)
            throw new InvalidDataException("白板文档画布尺寸无效。");
        if (document.Pages.Count is < 1 or > MaxPageCount ||
            document.CurrentPageIndex < 0 || document.CurrentPageIndex >= document.Pages.Count)
            throw new InvalidDataException("白板文档页面信息无效。");
        if (document.Assets.Count > MaxAssetCount)
            throw new InvalidDataException("白板文档图片资产过多。");

        var pageIds = new HashSet<string>(StringComparer.Ordinal);
        var referencedAssetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in document.Pages)
        {
            if (!IsPageId(page.Id) || !pageIds.Add(page.Id) ||
                !IsSafeText(page.Title, 256) || !IsSafeText(page.Background, 128) ||
                page.Data.Length == 0 || page.Data.Length > MaxPageBytes)
                throw new InvalidDataException("白板文档页面信息无效。");
            ValidatePageData(page.Data, new PageManifest(page.Id, page.Title, page.Background));

            var parsedPage = PageSerializer.Deserialize(page.Data, page.Id, page.Title);
            foreach (var image in parsedPage.GetAllObjects().OfType<ImageObject>())
                referencedAssetIds.Add(image.AssetId);
        }

        if (!document.Assets.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(referencedAssetIds))
            throw new InvalidDataException("白板文档图片资产与页面引用不一致。");

        foreach (var (assetId, data) in document.Assets)
        {
            if (!IsAssetId(assetId) || data.Length == 0 || data.Length > MaxAssetBytes ||
                !ImageAssetValidator.TryValidate(data, out _) ||
                !string.Equals(RawImageAssetStore.ComputeAssetId(data), assetId, StringComparison.Ordinal))
                throw new InvalidDataException("白板文档图片资产无效。");
        }
    }

    private static void ValidateCentralDirectoryEntryCount(byte[] package)
    {
        const uint endOfCentralDirectorySignature = 0x06054B50;
        const uint zip64LocatorSignature = 0x07064B50;
        const uint zip64EndOfCentralDirectorySignature = 0x06064B50;
        const int endOfCentralDirectorySize = 22;
        const int zip64LocatorSize = 20;
        const int maximumCommentLength = ushort.MaxValue;

        var firstCandidate = Math.Max(0, package.Length - endOfCentralDirectorySize - maximumCommentLength);
        for (var offset = package.Length - endOfCentralDirectorySize; offset >= firstCandidate; offset--)
        {
            var candidate = package.AsSpan(offset);
            if (BinaryPrimitives.ReadUInt32LittleEndian(candidate) != endOfCentralDirectorySignature ||
                BinaryPrimitives.ReadUInt16LittleEndian(candidate[20..]) != package.Length - offset - endOfCentralDirectorySize)
                continue;

            var entryCount = BinaryPrimitives.ReadUInt16LittleEndian(candidate[10..]);
            if (entryCount != ushort.MaxValue)
            {
                ValidateArchiveEntryCount(entryCount);
                return;
            }

            if (offset < zip64LocatorSize ||
                BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(offset - zip64LocatorSize)) != zip64LocatorSignature)
                throw new InvalidDataException("ZIP64 文档缺少目录定位器。");

            var locator = package.AsSpan(offset - zip64LocatorSize);
            var zip64Offset = BinaryPrimitives.ReadUInt64LittleEndian(locator[8..]);
            if (zip64Offset > int.MaxValue || zip64Offset > (ulong)(package.Length - 56))
                throw new InvalidDataException("ZIP64 文档目录位置无效。");

            var zip64Directory = package.AsSpan((int)zip64Offset);
            if (BinaryPrimitives.ReadUInt32LittleEndian(zip64Directory) != zip64EndOfCentralDirectorySignature ||
                BinaryPrimitives.ReadUInt64LittleEndian(zip64Directory[4..]) < 44)
                throw new InvalidDataException("ZIP64 文档目录无效。");

            ValidateArchiveEntryCount(BinaryPrimitives.ReadUInt64LittleEndian(zip64Directory[32..]));
            return;
        }

        throw new InvalidDataException("白板文档 ZIP 目录无效。");
    }

    private static void ValidateArchiveEntryCount(ulong entryCount)
    {
        if (entryCount is 0 or > MaxArchiveEntryCount)
            throw new InvalidDataException("白板文档条目数量无效。");
    }

    private static Dictionary<string, byte[]> ReadEntries(ZipArchive archive)
    {
        if (archive.Entries.Count is < 1 or > MaxArchiveEntryCount)
            throw new InvalidDataException("白板文档条目数量无效。");

        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long totalSize = 0;
        foreach (var entry in archive.Entries)
        {
            ValidateEntryName(entry.FullName);
            if (!entries.TryAdd(entry.FullName, []))
                throw new InvalidDataException("白板文档包含重复条目。");
            var maxEntryBytes = GetMaxEntryBytes(entry.FullName);
            if (entry.Length < 0 || entry.Length > maxEntryBytes || totalSize > MaxPackageBytes - entry.Length)
                throw new InvalidDataException("白板文档解压后大小无效。");

            var content = ReadEntry(entry, (int)entry.Length);
            entries[entry.FullName] = content;
            totalSize += content.Length;
        }
        return entries;
    }

    internal static void ValidateEntryLengthForTesting(string entryName, long entryLength)
    {
        ValidateEntryName(entryName);
        if (entryLength < 0 || entryLength > GetMaxEntryBytes(entryName))
            throw new InvalidDataException("白板文档解压后大小无效。");
    }

    private static long GetMaxEntryBytes(string entryName) =>
        entryName == ManifestEntryName
            ? MaxManifestBytes
            : entryName.StartsWith("pages/", StringComparison.Ordinal)
                ? MaxPageBytes
                : entryName.StartsWith("assets/", StringComparison.Ordinal)
                    ? MaxAssetBytes
                    : 0;

    private static byte[] ReadEntry(ZipArchiveEntry entry, int length)
    {
        var data = new byte[length];
        using var stream = entry.Open();
        int offset = 0;
        while (offset < data.Length)
        {
            var read = stream.Read(data, offset, data.Length - offset);
            if (read == 0)
                throw new InvalidDataException("白板文档条目内容截断。");
            offset += read;
        }
        if (stream.ReadByte() != -1)
            throw new InvalidDataException("白板文档条目长度无效。");
        return data;
    }

    private static DocumentManifest DeserializeManifest(byte[] data)
    {
        if (data.Length == 0 || data.Length > MaxManifestBytes)
            throw new InvalidDataException("白板文档清单大小无效。");
        try
        {
            return JsonSerializer.Deserialize<DocumentManifest>(data, JsonOptions)
                ?? throw new InvalidDataException("白板文档清单为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("白板文档清单格式无效。", ex);
        }
    }

    private static void ValidateManifest(DocumentManifest manifest)
    {
        if (manifest.SchemaVersion != SchemaVersion || !IsDocumentId(manifest.DocumentId) ||
            !float.IsFinite(manifest.CanvasWidth) || manifest.CanvasWidth <= 0 ||
            !float.IsFinite(manifest.CanvasHeight) || manifest.CanvasHeight <= 0 ||
            manifest.Pages is null || manifest.Pages.Length is < 1 or > MaxPageCount ||
            manifest.CurrentPageIndex < 0 || manifest.CurrentPageIndex >= manifest.Pages.Length)
            throw new InvalidDataException("白板文档清单字段无效。");

        var pageIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in manifest.Pages)
        {
            if (!IsPageId(page.Id) || !pageIds.Add(page.Id) ||
                !IsSafeText(page.Title, 256) || !IsSafeText(page.Background, 128))
                throw new InvalidDataException("白板文档清单页面字段无效。");
        }
    }

    private static void ValidatePageData(byte[] data, PageManifest page)
    {
        if (data.Length == 0 || data.Length > MaxPageBytes)
            throw new InvalidDataException("白板页面数据大小无效。");

        try
        {
            var parsed = PageSerializer.Deserialize(data, page.Id, page.Title);
            if (parsed.Id != page.Id || !PageSerializer.Serialize(parsed).AsSpan().SequenceEqual(data))
                throw new InvalidDataException("白板页面数据损坏或不受支持。");

            ValidatePageObjects(parsed);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("白板页面数据格式无效。", ex);
        }
    }

    private static void ValidatePageObjects(PageModel page)
    {
        var objects = page.GetAllObjects();
        if (objects.Count > MaxObjectsPerPage)
            throw new InvalidDataException("白板页面对象数量超过限制。");

        var objectIds = new HashSet<Guid>();
        foreach (var obj in objects)
        {
            if (!objectIds.Add(obj.Id) || !IsValidBounds(obj) ||
                obj.Transform.Length != 6 || obj.Transform.Any(value => !float.IsFinite(value)))
                throw new InvalidDataException("白板页面包含无效对象几何数据。");

            switch (obj)
            {
                case StrokeObject stroke:
                    if (!IsValidStroke(stroke))
                        throw new InvalidDataException("白板页面包含无效笔画数据。");
                    break;
                case ImageObject image:
                    if (!IsAssetId(image.AssetId) || !IsPositiveFinite(image.OriginalWidth) ||
                        !IsPositiveFinite(image.OriginalHeight))
                        throw new InvalidDataException("白板页面包含无效图片数据。");
                    break;
                case TextObject text:
                    if (text.Content.Length > 65_536 || string.IsNullOrWhiteSpace(text.Style.FontFamily) ||
                        text.Style.FontFamily.Length > 256 || !IsPositiveFinite(text.Style.FontSize))
                        throw new InvalidDataException("白板页面包含无效文本数据。");
                    break;
                case ShapeObject shape:
                    if (!Enum.IsDefined(shape.Type) || !IsPositiveFinite(shape.StrokeWidth))
                        throw new InvalidDataException("白板页面包含无效形状数据。");
                    break;
                case PathObject path:
                    if (path.PathData.Length > 65_536 || !IsPositiveFinite(path.StrokeWidth))
                        throw new InvalidDataException("白板页面包含无效路径数据。");
                    break;
                case DocumentObject document when document.PageCount < 0 || document.FileRef.Length > 4_096:
                    throw new InvalidDataException("白板页面包含无效文档数据。");
            }
        }
    }

    private static bool IsValidBounds(WhiteboardObject obj) =>
        IsFiniteCoordinate(obj.BoundsX) && IsFiniteCoordinate(obj.BoundsY) &&
        IsPositiveFinite(obj.BoundsWidth) && IsPositiveFinite(obj.BoundsHeight);

    private static bool IsValidStroke(StrokeObject stroke) =>
        Enum.IsDefined(stroke.Style.PenType) && Enum.IsDefined(stroke.Style.InkType) &&
        IsPositiveFinite(stroke.Style.Thickness) && stroke.Points.Count <= 1_000_000 &&
        stroke.Points.All(point =>
            IsFiniteCoordinate(point.X) && IsFiniteCoordinate(point.Y) &&
            float.IsFinite(point.Pressure));

    private static bool IsFiniteCoordinate(float value) =>
        float.IsFinite(value) && MathF.Abs(value) <= MaxAbsoluteCoordinate;

    private static bool IsPositiveFinite(float value) =>
        float.IsFinite(value) && value > 0 && value <= MaxAbsoluteCoordinate;

    private static void ValidateEntryName(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) || entryName.Length > 512 ||
            entryName.Contains('\\') || entryName.StartsWith("/", StringComparison.Ordinal) ||
            entryName.EndsWith("/", StringComparison.Ordinal) || entryName.Split('/').Any(part => part is "." or ".."))
            throw new InvalidDataException("白板文档包含不安全条目名称。");
    }

    private static string GetPageEntryName(string pageId) => $"pages/{pageId}.bin";
    private static string GetAssetEntryName(string assetId) => $"assets/{assetId}";
    private static bool IsDocumentId(string value) => IsIdentifier(value, 64);
    private static bool IsPageId(string value) => IsIdentifier(value, 64);
    private static bool IsAssetId(string value) => value.Length == 32 && value.All(Uri.IsHexDigit);
    private static bool IsIdentifier(string value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    private static bool IsSafeText(string value, int maxLength) =>
        value is not null && value.Length <= maxLength && !value.Any(char.IsControl);

    private static void WriteEntry(ZipArchive archive, string entryName, byte[] data)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(data);
    }

    private sealed record DocumentManifest(
        int SchemaVersion,
        string DocumentId,
        float CanvasWidth,
        float CanvasHeight,
        int CurrentPageIndex,
        PageManifest[] Pages);

    private sealed record PageManifest(string Id, string Title, string Background);
}
