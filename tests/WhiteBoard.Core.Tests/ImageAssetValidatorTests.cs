using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using WhiteBoard.Core.Services;

namespace WhiteBoard.Core.Tests;

public sealed class ImageAssetValidatorTests
{
    [Theory]
    [MemberData(nameof(ValidImages))]
    public void TryValidate_CompleteSupportedImage_ReturnsMetadata(
        ImageAssetFormat format, byte[] image, int width, int height)
    {
        var accepted = ImageAssetValidator.TryValidate(image, out var info);

        Assert.True(accepted);
        Assert.NotNull(info);
        Assert.Equal(format, info.Format);
        Assert.Equal(width, info.Width);
        Assert.Equal(height, info.Height);
    }

    [Theory]
    [MemberData(nameof(ValidImages))]
    public void TryValidate_TruncatedSupportedImage_IsRejected(
        ImageAssetFormat format, byte[] image, int width, int height)
    {
        _ = format;
        _ = width;
        _ = height;
        Assert.False(ImageAssetValidator.TryValidate(image[..^1], out ImageAssetInfo? _));
    }

    [Fact]
    public void TryValidate_Adam7Png_ReturnsMetadata()
    {
        var accepted = ImageAssetValidator.TryValidate(CreatePng(1, 1, interlaced: true), out var info);

        Assert.True(accepted);
        Assert.NotNull(info);
        Assert.Equal(ImageAssetFormat.Png, info.Format);
        Assert.Equal(1, info.Width);
        Assert.Equal(1, info.Height);
    }

    [Fact]
    public void TryValidate_PngWithInvalidChunkCrc_IsRejected()
    {
        var image = CreatePng(1, 1);
        image[29] ^= 0x01;

        Assert.False(ImageAssetValidator.TryValidate(image, out _));
    }

    [Fact]
    public void TryValidate_PngWithTrailingBytes_IsRejected()
    {
        var image = CreatePng(1, 1).Concat(new byte[] { 0x00 }).ToArray();

        Assert.False(ImageAssetValidator.TryValidate(image, out _));
    }

    [Fact]
    public void TryValidate_JpegWithoutSos_IsRejected()
    {
        var image = CreateJpeg();
        var withoutSos = image[..^6];

        Assert.False(ImageAssetValidator.TryValidate(withoutSos, out _));
    }

    [Fact]
    public void TryValidate_GifWithoutTrailer_IsRejected()
    {
        var image = CreateGif();

        Assert.False(ImageAssetValidator.TryValidate(image[..^1], out _));
    }

    [Fact]
    public void TryValidate_BmpWithTruncatedPixelArray_IsRejected()
    {
        var image = CreateBmp();

        Assert.False(ImageAssetValidator.TryValidate(image[..^1], out _));
    }

    [Fact]
    public void TryValidate_PngWithExcessivePixels_IsRejected()
    {
        Assert.False(ImageAssetValidator.TryValidate(CreatePngHeader(8_192, 8_192), out _));
    }

    [Fact]
    public void TryStore_InvalidImage_DoesNotPolluteStore()
    {
        var malformed = CreatePng(1, 1)[..^1];
        var assetId = RawImageAssetStore.ComputeAssetId(malformed);

        Assert.False(RawImageAssetStore.TryStore(assetId, malformed));
        Assert.Null(RawImageAssetStore.Retrieve(assetId));
    }

    [Fact]
    public void Store_ValidImage_DefensivelyCopiesBytes()
    {
        var image = CreatePng(1, 1);
        var assetId = RawImageAssetStore.ComputeAssetId(image);

        RawImageAssetStore.Store(assetId, image);
        image[16] = 99;

        var stored = Assert.IsType<byte[]>(RawImageAssetStore.Retrieve(assetId));
        Assert.Equal((byte)0, stored[16]);
        stored[16] = 77;
        Assert.Equal((byte)0, RawImageAssetStore.Retrieve(assetId)![16]);
    }

    public static IEnumerable<object[]> ValidImages()
    {
        yield return [ImageAssetFormat.Png, CreatePng(2, 3), 2, 3];
        yield return [ImageAssetFormat.Jpeg, CreateJpeg(), 1, 1];
        yield return [ImageAssetFormat.Gif, CreateGif(), 1, 1];
        yield return [ImageAssetFormat.Bmp, CreateBmp(), 1, 1];
    }

    private static byte[] CreatePngHeader(uint width, uint height)
    {
        var data = new byte[33];
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        signature.CopyTo(data);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 13);
        "IHDR"u8.CopyTo(data.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), height);
        data[24] = 8;
        data[25] = 6;
        return data;
    }

    internal static byte[] CreatePng(uint width = 1, uint height = 1, bool interlaced = false)
    {
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        ihdr[12] = interlaced ? (byte)1 : (byte)0;
        WritePngChunk(output, "IHDR"u8, ihdr);

        var scanlines = interlaced
            ? new byte[] { 0, 0, 0, 0, 0 }
            : new byte[checked(((int)width * 4 + 1) * (int)height)];
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(scanlines);
        WritePngChunk(output, "IDAT"u8, compressed.ToArray());
        WritePngChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    internal static byte[] CreateJpeg()
    {
        // 1×1 基线 JPEG：SOI、APP0、SOF0、SOS、一个熵编码字节和 EOI。
        return
        [
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
            0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00,
            0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00,
            0x00, 0xFF, 0xD9,
        ];
    }

    internal static byte[] CreateGif()
    {
        return
        [
            .. "GIF89a"u8.ToArray(),
            0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00,
            0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF,
            0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
            0x02, 0x02, 0x4C, 0x01, 0x00,
            0x3B,
        ];
    }

    internal static byte[] CreateBmp()
    {
        var image = new byte[58];
        image[0] = (byte)'B';
        image[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(2), (uint)image.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(10), 54);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(18), 1);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(28), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(34), 4);
        image[54] = 0xFF;
        image[55] = 0xFF;
        image[56] = 0xFF;
        return image;
    }

    private static void WritePngChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> payload)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)payload.Length));
        stream.Write(length);
        stream.Write(type);
        stream.Write(payload);

        var crcInput = new byte[type.Length + payload.Length];
        type.CopyTo(crcInput);
        payload.CopyTo(crcInput.AsSpan(type.Length));
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32.HashToUInt32(crcInput));
        stream.Write(crc);
    }
}
