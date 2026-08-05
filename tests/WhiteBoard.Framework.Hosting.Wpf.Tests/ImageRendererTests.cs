using System.IO;
using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WhiteBoard.Core.Domain.Objects;
using WhiteBoard.Core.Services;
using WhiteBoard.UI.Rendering;

namespace WhiteBoard.Framework.Hosting.Wpf.Tests;

public sealed class ImageRendererTests
{
    [Fact]
    public async Task RenderObject_RawAssetFallback_DrawsImageAndCachesBitmap()
    {
        await Sta.RunAsync(() =>
        {
            var bytes = CreateRedPng();
            var assetId = RawImageAssetStore.ComputeAssetId(bytes);
            RawImageAssetStore.Store(assetId, bytes);
            var image = new ImageObject(assetId, 4f, 4f, 12f, 12f);

            var pixels = Render(image);

            Assert.True(pixels[2] > 150, "图片中心像素应包含红色通道。");
            Assert.True(pixels[2] > pixels[1] + 50, "图片中心像素不应是占位符或白色背景。");
            Assert.NotNull(ImageAssetStore.Retrieve(assetId));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task RenderObject_PaddedLegacyAssetId_UsesRawAssetAlias()
    {
        await Sta.RunAsync(() =>
        {
            var bytes = CreateRedPng();
            var canonicalAssetId = RawImageAssetStore.ComputeAssetId(bytes);
            var legacyAssetId = canonicalAssetId[..16];
            var paddedLegacyAssetId = legacyAssetId.PadRight(32, '0');
            RawImageAssetStore.Store(legacyAssetId, bytes);
            var image = new ImageObject(paddedLegacyAssetId, 4f, 4f, 12f, 12f);

            var pixels = Render(image);

            Assert.True(pixels[2] > 150, "兼容 asset ID 应渲染原始图片。");
            Assert.NotNull(ImageAssetStore.Retrieve(paddedLegacyAssetId));
            return Task.CompletedTask;
        });
    }

    private static byte[] Render(ImageObject image)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, 20, 20));
            InkRenderer.RenderObject(context, image);
        }

        var bitmap = new RenderTargetBitmap(20, 20, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[4];
        bitmap.CopyPixels(new Int32Rect(10, 10, 1, 1), pixels, 4, 0);
        return pixels;
    }

    private static byte[] CreateRedPng()
    {
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, 1);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], 1);
        header[8] = 8;
        header[9] = 6;
        WritePngChunk(output, "IHDR"u8, header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write([0, 255, 0, 0, 255]);
        WritePngChunk(output, "IDAT"u8, compressed.ToArray());
        WritePngChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void WritePngChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);
        output.Write(type);
        output.Write(data);

        var crcInput = new byte[type.Length + data.Length];
        type.CopyTo(crcInput);
        data.CopyTo(crcInput.AsSpan(type.Length));
        BinaryPrimitives.WriteUInt32BigEndian(length, Crc32.HashToUInt32(crcInput));
        output.Write(length);
    }
}
