using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;
using WhiteBoard.Core.Services;

namespace WhiteBoard.Core.Tests;

public sealed class WhiteboardSessionBoundaryTests
{
    [Theory]
    [InlineData(0f, 1080f)]
    [InlineData(-1f, 1080f)]
    [InlineData(1920f, 0f)]
    [InlineData(float.NaN, 1080f)]
    [InlineData(1920f, float.PositiveInfinity)]
    public async Task SetCanvasSizeAsync_RejectsNonFiniteOrNonPositiveDimensions(float width, float height)
    {
        await using var session = new WhiteboardSession();
        await session.StartAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => session.SetCanvasSizeAsync(width, height));
    }

    [Fact]
    public async Task SetCanvasSizeAsync_UpdatesDimensionsAsOneExecutorOperation()
    {
        await using var session = new WhiteboardSession();
        await session.StartAsync();

        await session.SetCanvasSizeAsync(1440f, 900f);

        var size = await session.GetCanvasSizeAsync();
        Assert.Equal(1440f, size.Width);
        Assert.Equal(900f, size.Height);
    }

    [Fact]
    public async Task ReplaceDocumentAsync_InvalidAssets_LeavesExistingDocumentUntouched()
    {
        await using var source = new WhiteboardSession();
        await source.StartAsync();
        var imageData = ImageAssetValidatorTests.CreatePng();
        var assetId = RawImageAssetStore.ComputeAssetId(imageData);
        RawImageAssetStore.Store(assetId, imageData);
        await source.AddImageAsync(new ImageObject(assetId, 10f, 10f, 20f, 20f));
        var candidate = await source.CaptureDocumentAsync();

        await using var target = new WhiteboardSession();
        await target.StartAsync();
        await target.AddShapeAsync(new ShapeObject(
            ShapeType.Rectangle, 0xFFFFFFFF, 0xFF000000, 1f, true,
            1f, 2f, 30f, 30f));
        var before = await target.CaptureDocumentAsync();

        var oversizedAsset = ImageAssetValidatorTests.CreatePng(8_192, 8_192);
        var invalid = candidate with
        {
            Assets = new Dictionary<string, byte[]>
            {
                [RawImageAssetStore.ComputeAssetId(oversizedAsset)] = oversizedAsset,
            },
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => target.ReplaceDocumentAsync(invalid));

        var after = await target.CaptureDocumentAsync();
        Assert.Equal(before.DocumentId, after.DocumentId);
        Assert.Equal(before.Pages[0].Data, after.Pages[0].Data);
    }

    [Fact]
    public async Task CaptureDocumentAsync_NormalizesLegacyImageAssetIdForPersistence()
    {
        var imageData = ImageAssetValidatorTests.CreatePng();
        var canonicalAssetId = RawImageAssetStore.ComputeAssetId(imageData);
        var legacyAssetId = canonicalAssetId[..16];
        Assert.True(RawImageAssetStore.TryStore(legacyAssetId, imageData));

        await using var session = new WhiteboardSession();
        await session.StartAsync();
        await session.AddImageAsync(new ImageObject(legacyAssetId, 10f, 10f, 20f, 20f));

        var document = await session.CaptureDocumentAsync();
        var page = PageSerializer.Deserialize(document.Pages[0].Data, document.Pages[0].Id);
        var image = Assert.IsType<ImageObject>(Assert.Single(page.GetAllObjects()));

        Assert.Equal(canonicalAssetId, image.AssetId);
        Assert.Contains(canonicalAssetId, document.Assets.Keys);
        Assert.NotNull(WhiteboardDocumentSerializer.Deserialize(WhiteboardDocumentSerializer.Serialize(document)));
    }

    [Fact]
    public async Task UndoAfterDeletingImage_RawAssetRemainsAvailable()
    {
        await using var session = new WhiteboardSession();
        await session.StartAsync();
        var imageData = ImageAssetValidatorTests.CreatePng();
        var assetId = RawImageAssetStore.ComputeAssetId(imageData);
        RawImageAssetStore.Store(assetId, imageData);
        var image = new ImageObject(assetId, 10f, 10f, 20f, 20f);
        await session.AddImageAsync(image);

        await session.DeleteObjectsAsync([image.Id]);
        await session.UndoAsync();

        Assert.Equal(imageData, RawImageAssetStore.Retrieve(assetId));
        var document = await session.CaptureDocumentAsync();
        Assert.Contains(assetId, document.Assets.Keys);
    }
}