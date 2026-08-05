using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using WhiteBoard.Contracts.Services;
using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;
using WhiteBoard.Core.Services;

namespace WhiteBoard.Core.Tests;

public sealed class WhiteboardDocumentSerializerTests
{
    [Fact]
    public async Task SerializeDeserializeAndReplaceDocument_RoundTripsPagesCanvasAndImageAssets()
    {
        var imageData = CreateValidPng();
        var assetId = RawImageAssetStore.ComputeAssetId(imageData);
        RawImageAssetStore.Store(assetId, imageData);

        await using var source = new WhiteboardSession();
        await source.StartAsync();
        await source.SetCanvasSizeAsync(1440f, 900f);
        await source.AddShapeAsync(new ShapeObject(
            ShapeType.Rectangle, 0xFFFFFFFF, 0xFF112233, 2f, true,
            10f, 20f, 100f, 80f));
        await source.AddImageAsync(new ImageObject(assetId, 150f, 40f, 120f, 90f));
        await source.AddPageAsync("第二页");
        await source.AddTextObjectAsync(new TextObject(
            "已恢复", new TextStyle("Arial", 20f, false, false, 0xFF000000),
            20f, 30f, 200f, 50f));

        var package = WhiteboardDocumentSerializer.Serialize(await source.CaptureDocumentAsync());
        var restored = WhiteboardDocumentSerializer.Deserialize(package);

        await using var target = new WhiteboardSession();
        await target.StartAsync();
        await target.ReplaceDocumentAsync(restored);

        var captured = await target.CaptureDocumentAsync();
        Assert.Equal(1440f, captured.CanvasSize.Width);
        Assert.Equal(900f, captured.CanvasSize.Height);
        Assert.Equal(2, captured.Pages.Count);
        Assert.Equal(1, captured.CurrentPageIndex);
        Assert.Equal("第二页", captured.Pages[1].Title);
        Assert.Equal(imageData, RawImageAssetStore.Retrieve(assetId));
    }

    [Fact]
    public async Task Serialize_DocumentWithUnreferencedAsset_IsRejected()
    {
        await using var session = new WhiteboardSession();
        await session.StartAsync();
        var document = await session.CaptureDocumentAsync();

        var assetData = new byte[] { 9, 8, 7, 6 };
        var invalid = document with
        {
            Assets = new Dictionary<string, byte[]>
            {
                [RawImageAssetStore.ComputeAssetId(assetData)] = assetData,
            },
        };

        Assert.Throws<InvalidDataException>(() => WhiteboardDocumentSerializer.Serialize(invalid));
    }

    [Fact]
    public async Task ReplaceDocumentAsync_InvalidCandidate_LeavesExistingDocumentUntouched()
    {
        await using var session = new WhiteboardSession();
        await session.StartAsync();
        await session.AddShapeAsync(new ShapeObject(
            ShapeType.Circle, 0xFFFFFFFF, 0xFF000000, 1f, true,
            1f, 2f, 30f, 30f));
        var before = await session.CaptureDocumentAsync();

        var invalid = new WhiteboardDocumentSnapshot(
            "invalid", new CanvasSizeSnapshot(800f, 600f),
            [new WhiteboardDocumentPageSnapshot("page", "Page", "white", [1, 2, 3])],
            0, new Dictionary<string, byte[]>());

        await Assert.ThrowsAsync<InvalidDataException>(() => session.ReplaceDocumentAsync(invalid));

        var after = await session.CaptureDocumentAsync();
        Assert.Equal(before.DocumentId, after.DocumentId);
        Assert.Equal(before.Pages[0].Data, after.Pages[0].Data);
    }

    [Fact]
    public void Deserialize_MissingManifest_RejectsPackage()
    {
        var package = WhiteboardDocumentSerializer.CreatePackageForTesting("pages/page.bin", [1, 2, 3]);

        Assert.Throws<InvalidDataException>(() => WhiteboardDocumentSerializer.Deserialize(package));
    }

    [Fact]
    public void Deserialize_DuplicateEntries_RejectsPackage()
    {
        var package = RewritePackage(
            WhiteboardDocumentSerializer.CreatePackageForTesting("manifest.json", []),
            static (_, data) => data,
            ("manifest.json", Array.Empty<byte>()));

        Assert.Throws<InvalidDataException>(() => WhiteboardDocumentSerializer.Deserialize(package));
    }

    [Fact]
    public async Task Deserialize_MissingReferencedAsset_RejectsPackage()
    {
        var (package, assetId) = await CreateImageDocumentPackageAsync();
        var withoutAsset = RewritePackage(
            package,
            (entryName, data) => entryName == $"assets/{assetId}" ? null : data);

        Assert.Throws<InvalidDataException>(() => WhiteboardDocumentSerializer.Deserialize(withoutAsset));
    }

    [Fact]
    public async Task Deserialize_AssetHashMismatch_RejectsPackage()
    {
        var (package, assetId) = await CreateImageDocumentPackageAsync();
        var corruptedAsset = RewritePackage(
            package,
            (entryName, data) => entryName == $"assets/{assetId}" ? [9, 9, 9] : data);

        Assert.Throws<InvalidDataException>(() => WhiteboardDocumentSerializer.Deserialize(corruptedAsset));
    }

    [Fact]
    public async Task Deserialize_HashCorrectButTruncatedImage_RejectsPackage()
    {
        var (package, assetId) = await CreateImageDocumentPackageAsync();
        var truncated = ImageAssetValidatorTests.CreatePng()[..^1];
        var malformed = RewritePackage(package, (entryName, data) =>
            entryName == $"assets/{assetId}" ? truncated : data);

        Assert.Throws<InvalidDataException>(() => WhiteboardDocumentSerializer.Deserialize(malformed));
    }

    [Fact]
    public async Task Deserialize_UnexpectedEntry_RejectsPackage()
    {
        var (package, _) = await CreateImageDocumentPackageAsync();
        var packageWithUnexpectedEntry = RewritePackage(
            package,
            static (_, data) => data,
            ("notes.txt", [1]));

        Assert.Throws<InvalidDataException>(() => WhiteboardDocumentSerializer.Deserialize(packageWithUnexpectedEntry));
    }

    [Fact]
    public async Task Deserialize_StrokePointCountExceedsPayload_RejectsPackage()
    {
        var package = await CreateStrokeDocumentPackageAsync();
        var malformed = RewriteFirstPageChunk(package, chunk =>
        {
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(84), uint.MaxValue);
            RewriteChunkCrc(chunk);
        });

        Assert.Throws<InvalidDataException>(() => WhiteboardDocumentSerializer.Deserialize(malformed));
    }

    [Fact]
    public async Task Deserialize_ObjectCountOverLimit_RejectsPackage()
    {
        var package = await CreateShapeDocumentPackageAsync();
        var malformed = RewritePackage(package, (entryName, data) =>
        {
            if (!entryName.StartsWith("pages/", StringComparison.Ordinal))
                return data;

            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(10), 50_001u);
            var headerCrc = Crc32.HashToUInt32(data.AsSpan(0, 14));
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(14), headerCrc);
            return data;
        });

        Assert.Throws<InvalidDataException>(() => WhiteboardDocumentSerializer.Deserialize(malformed));
    }

    [Fact]
    public async Task Deserialize_NonFiniteShapeGeometry_RejectsPackage()
    {
        var package = await CreateShapeDocumentPackageAsync();
        var malformed = RewriteFirstPageChunk(package, chunk =>
        {
            BinaryPrimitives.WriteSingleLittleEndian(chunk.AsSpan(28), float.NaN);
            RewriteChunkCrc(chunk);
        });

        Assert.Throws<InvalidDataException>(() => WhiteboardDocumentSerializer.Deserialize(malformed));
    }

    [Fact]
    public async Task Deserialize_NonFiniteTransformAndStrokePoint_RejectsPackage()
    {
        var shapePackage = await CreateShapeDocumentPackageAsync();
        var invalidTransform = RewriteFirstPageChunk(shapePackage, chunk =>
        {
            BinaryPrimitives.WriteSingleLittleEndian(chunk.AsSpan(44), float.PositiveInfinity);
            RewriteChunkCrc(chunk);
        });
        Assert.Throws<InvalidDataException>(() => WhiteboardDocumentSerializer.Deserialize(invalidTransform));

        var strokePackage = await CreateStrokeDocumentPackageAsync();
        var invalidStrokePoint = RewriteFirstPageChunk(strokePackage, chunk =>
        {
            BinaryPrimitives.WriteSingleLittleEndian(chunk.AsSpan(88), float.NaN);
            RewriteChunkCrc(chunk);
        });
        Assert.Throws<InvalidDataException>(() => WhiteboardDocumentSerializer.Deserialize(invalidStrokePoint));
    }

    [Fact]
    public async Task ReplaceDocumentAsync_NonFiniteGeometry_LeavesExistingDocumentUntouched()
    {
        await using var session = new WhiteboardSession();
        await session.StartAsync();
        await session.AddShapeAsync(new ShapeObject(
            ShapeType.Circle, 0xFFFFFFFF, 0xFF000000, 1f, true,
            1f, 2f, 30f, 30f));
        var before = await session.CaptureDocumentAsync();

        var invalidPageData = RewriteFirstChunk(before.Pages[0].Data, chunk =>
        {
            BinaryPrimitives.WriteSingleLittleEndian(chunk.AsSpan(28), float.PositiveInfinity);
            RewriteChunkCrc(chunk);
        });
        var invalid = before with
        {
            Pages = [before.Pages[0] with { Data = invalidPageData }],
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => session.ReplaceDocumentAsync(invalid));

        var after = await session.CaptureDocumentAsync();
        Assert.Equal(before.DocumentId, after.DocumentId);
        Assert.Equal(before.Pages[0].Data, after.Pages[0].Data);
    }

    [Fact]
    public void EntryLengthLimits_AllowPagesLargerThanAssets()
    {
        WhiteboardDocumentSerializer.ValidateEntryLengthForTesting("pages/page.bin", 24L * 1024 * 1024);

        Assert.Throws<InvalidDataException>(() =>
            WhiteboardDocumentSerializer.ValidateEntryLengthForTesting("assets/image", 24L * 1024 * 1024));
    }

    [Fact]
    public void Deserialize_TooManyArchiveEntries_RejectsPackage()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var index = 0; index < 1_202; index++)
                WriteEntry(archive, $"entries/{index}", []);
        }

        Assert.Throws<InvalidDataException>(() => WhiteboardDocumentSerializer.Deserialize(output.ToArray()));
    }

    [Fact]
    public async Task Deserialize_ExtremeFiniteBounds_RejectsPackage()
    {
        var package = await CreateShapeDocumentPackageAsync();
        var malformed = RewriteFirstPageChunk(package, chunk =>
        {
            BinaryPrimitives.WriteSingleLittleEndian(chunk.AsSpan(28), float.MaxValue);
            BinaryPrimitives.WriteSingleLittleEndian(chunk.AsSpan(36), float.MaxValue);
            RewriteChunkCrc(chunk);
        });

        Assert.Throws<InvalidDataException>(() => WhiteboardDocumentSerializer.Deserialize(malformed));
    }

    [Fact]
    public async Task Deserialize_ImageWithExcessivePixels_RejectsPackage()
    {
        var (package, _) = await CreateImageDocumentPackageAsync();
        var oversizedAsset = ImageAssetValidatorTests.CreatePng(8_192, 8_192);
        var malformed = RewritePackage(
            package,
            (entryName, data) => entryName.StartsWith("assets/", StringComparison.Ordinal)
                ? oversizedAsset
                : data);

        Assert.Throws<InvalidDataException>(() => WhiteboardDocumentSerializer.Deserialize(malformed));
    }

    private static byte[] CreateValidPng() => ImageAssetValidatorTests.CreatePng();

    private static async Task<byte[]> CreateStrokeDocumentPackageAsync()
    {
        await using var session = new WhiteboardSession();
        await session.StartAsync();
        await session.AddStrokeAsync(new StrokeObject(
            [new PressurePoint(1f, 2f, 0.5f, 0)],
            new StrokeStyle(PenType.Pen, InkType.Normal, 0xFF000000, 2f)));

        return WhiteboardDocumentSerializer.Serialize(await session.CaptureDocumentAsync());
    }

    private static async Task<byte[]> CreateShapeDocumentPackageAsync()
    {
        await using var session = new WhiteboardSession();
        await session.StartAsync();
        await session.AddShapeAsync(new ShapeObject(
            ShapeType.Rectangle, 0xFFFFFFFF, 0xFF000000, 1f, true,
            10f, 20f, 100f, 80f));

        return WhiteboardDocumentSerializer.Serialize(await session.CaptureDocumentAsync());
    }

    private static byte[] RewriteFirstChunk(byte[] pageData, Action<byte[]> rewriteChunk)
    {
        const int pageHeaderSize = 22;
        var rewritten = (byte[])pageData.Clone();
        var chunkLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(rewritten.AsSpan(pageHeaderSize)));
        var chunk = rewritten.AsSpan(pageHeaderSize, chunkLength).ToArray();
        rewriteChunk(chunk);
        chunk.CopyTo(rewritten, pageHeaderSize);
        var headerCrc = Crc32.HashToUInt32(rewritten.AsSpan(0, pageHeaderSize - 8));
        BinaryPrimitives.WriteUInt32LittleEndian(rewritten.AsSpan(14), headerCrc);
        return rewritten;
    }

    private static byte[] RewriteFirstPageChunk(byte[] package, Action<byte[]> rewriteChunk) =>
        RewritePackage(package, (entryName, data) =>
        {
            if (!entryName.StartsWith("pages/", StringComparison.Ordinal))
                return data;

            return RewriteFirstChunk(data, rewriteChunk);
        });

    private static void RewriteChunkCrc(byte[] chunk)
    {
        var chunkLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(chunk));
        Assert.Equal(chunkLength, chunk.Length);
        var crc = Crc32.HashToUInt32(chunk.AsSpan(4, chunkLength - 8));
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(chunkLength - 4), crc);
    }

    private static async Task<(byte[] Package, string AssetId)> CreateImageDocumentPackageAsync()
    {
        var imageData = CreateValidPng();
        var assetId = RawImageAssetStore.ComputeAssetId(imageData);
        RawImageAssetStore.Store(assetId, imageData);

        await using var session = new WhiteboardSession();
        await session.StartAsync();
        await session.AddImageAsync(new ImageObject(assetId, 10f, 10f, 100f, 80f));

        return (WhiteboardDocumentSerializer.Serialize(await session.CaptureDocumentAsync()), assetId);
    }

    private static byte[] RewritePackage(
        byte[] package,
        Func<string, byte[], byte[]?> transform,
        params (string EntryName, byte[] Data)[] additions)
    {
        using var input = new MemoryStream(package, writable: false);
        using var source = new ZipArchive(input, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                using var stream = entry.Open();
                using var content = new MemoryStream();
                stream.CopyTo(content);
                var data = transform(entry.FullName, content.ToArray());
                if (data is not null)
                    WriteEntry(target, entry.FullName, data);
            }

            foreach (var (entryName, data) in additions)
                WriteEntry(target, entryName, data);
        }
        return output.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string entryName, byte[] data)
    {
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        stream.Write(data);
    }
}
