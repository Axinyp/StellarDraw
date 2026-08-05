using System.Buffers.Binary;
using System.IO.Compression;

namespace WhiteBoard.Core.Services;

/// <summary>受支持图片格式的元数据。</summary>
public enum ImageAssetFormat
{
    Png,
    Jpeg,
    Gif,
    Bmp,
}

/// <summary>已验证图片资产的不可变元数据。</summary>
public sealed record ImageAssetInfo(ImageAssetFormat Format, int Width, int Height, int ByteLength);

/// <summary>
/// 在解码前完整验证受支持图片的容器结构、尺寸和扫描预算。
/// 验证通过仅表示输入在 Core 的资源边界内；显示层仍必须处理解码器兼容性失败。
/// </summary>
public static class ImageAssetValidator
{
    /// <summary>单个图片压缩字节的最大允许大小。</summary>
    public const int MaxCompressedBytes = 20 * 1024 * 1024;

    /// <summary>单边最大像素数。</summary>
    public const int MaxDimension = 8_192;

    /// <summary>单张图片最大总像素数。</summary>
    public const int MaxPixels = 32 * 1024 * 1024;

    private const int MaxPngChunks = 1_024;
    private const int MaxJpegMarkers = 4_096;
    private const int MaxGifBlocks = 16_384;
    private const int MaxGifFrames = 256;

    /// <summary>验证图片完整容器结构、尺寸和资源预算。</summary>
    public static bool TryValidate(byte[]? data, out ImageAssetInfo? info)
    {
        info = null;
        if (data is null || data.Length == 0 || data.Length > MaxCompressedBytes)
            return false;

        return TryReadPng(data, out info) ||
            TryReadJpeg(data, out info) ||
            TryReadGif(data, out info) ||
            TryReadBmp(data, out info);
    }

    private static bool TryReadPng(ReadOnlySpan<byte> data, out ImageAssetInfo? info)
    {
        info = null;
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (data.Length < 57 || !data[..8].SequenceEqual(signature))
            return false;

        var offset = 8;
        var chunks = 0;
        var sawHeader = false;
        uint width = 0;
        uint height = 0;
        long expectedScanlineBytes = 0;
        var sawData = false;
        var endedData = false;
        using var compressedData = new MemoryStream();

        while (offset < data.Length && ++chunks <= MaxPngChunks)
        {
            if (data.Length - offset < 12)
                return false;

            var length = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            if (length > data.Length - offset - 12)
                return false;

            var payloadLength = (int)length;
            var type = data.Slice(offset + 4, 4);
            var payload = data.Slice(offset + 8, payloadLength);
            var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 8 + payloadLength, 4));
            if (ComputeCrc32(type, payload) != expectedCrc)
                return false;

            offset += checked(payloadLength + 12);
            if (!sawHeader)
            {
                if (!type.SequenceEqual("IHDR"u8) || payloadLength != 13 ||
                    !TryReadPngHeader(payload, out width, out height, out expectedScanlineBytes))
                    return false;

                sawHeader = true;
                continue;
            }

            if (type.SequenceEqual("IDAT"u8))
            {
                if (endedData || payloadLength == 0)
                    return false;
                sawData = true;
                compressedData.Write(payload);
                continue;
            }

            if (sawData)
                endedData = true;

            if (type.SequenceEqual("IEND"u8))
            {
                if (!sawData || payloadLength != 0 || offset != data.Length)
                    return false;

                return TryValidatePngScanlines(compressedData, expectedScanlineBytes) &&
                    CreateInfo(ImageAssetFormat.Png, width, height, data.Length, out info);
            }

            // PNG 中首字母大写表示关键 chunk；未知关键 chunk 不可安全忽略。
            if ((type[0] & 0x20) == 0)
                return false;
        }

        return false;
    }

    private static bool TryReadPngHeader(
        ReadOnlySpan<byte> header,
        out uint width,
        out uint height,
        out long expectedScanlineBytes)
    {
        width = BinaryPrimitives.ReadUInt32BigEndian(header);
        height = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
        expectedScanlineBytes = 0;
        if (!IsWithinPixelBudget(width, height) || header[10] != 0 || header[11] != 0 || header[12] is not (0 or 1))
            return false;

        var bitsPerPixel = header[9] switch
        {
            0 when header[8] is 1 or 2 or 4 or 8 or 16 => header[8],
            2 when header[8] is 8 or 16 => 3 * header[8],
            3 when header[8] is 1 or 2 or 4 or 8 => header[8],
            4 when header[8] is 8 or 16 => 2 * header[8],
            6 when header[8] is 8 or 16 => 4 * header[8],
            _ => 0,
        };
        if (bitsPerPixel == 0)
            return false;

        try
        {
            expectedScanlineBytes = header[12] == 0
                ? CalculatePngScanlineBytes(width, height, bitsPerPixel)
                : CalculateAdam7ScanlineBytes(width, height, bitsPerPixel);
            return expectedScanlineBytes > 0;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static long CalculatePngScanlineBytes(uint width, uint height, int bitsPerPixel)
    {
        var rowBytes = checked(((long)width * bitsPerPixel + 7) / 8);
        return checked((rowBytes + 1) * height);
    }

    private static long CalculateAdam7ScanlineBytes(uint width, uint height, int bitsPerPixel)
    {
        ReadOnlySpan<(int X, int Y, int WidthStep, int HeightStep)> passes =
        [
            (0, 0, 8, 8),
            (4, 0, 8, 8),
            (0, 4, 4, 8),
            (2, 0, 4, 4),
            (0, 2, 2, 4),
            (1, 0, 2, 2),
            (0, 1, 1, 2),
        ];

        long totalBytes = 0;
        foreach (var (x, y, widthStep, heightStep) in passes)
        {
            var passWidth = DivideRoundUp((long)width - x, widthStep);
            var passHeight = DivideRoundUp((long)height - y, heightStep);
            if (passWidth == 0 || passHeight == 0)
                continue;

            var rowBytes = checked((passWidth * bitsPerPixel + 7) / 8);
            totalBytes = checked(totalBytes + (rowBytes + 1) * passHeight);
        }
        return totalBytes;
    }

    private static long DivideRoundUp(long value, int divisor) =>
        value <= 0 ? 0 : checked((value + divisor - 1) / divisor);

    private static bool TryValidatePngScanlines(MemoryStream compressedData, long expectedBytes)
    {
        try
        {
            compressedData.Position = 0;
            using var zlib = new ZLibStream(compressedData, CompressionMode.Decompress, leaveOpen: false);
            var buffer = new byte[81_920];
            long actualBytes = 0;
            int read;
            while ((read = zlib.Read(buffer, 0, buffer.Length)) != 0)
            {
                actualBytes += read;
                if (actualBytes > expectedBytes)
                    return false;
            }
            return actualBytes == expectedBytes;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool TryReadJpeg(ReadOnlySpan<byte> data, out ImageAssetInfo? info)
    {
        info = null;
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            return false;

        var offset = 2;
        var markers = 0;
        var sawFrame = false;
        var sawScan = false;
        uint width = 0;
        uint height = 0;

        while (offset < data.Length && ++markers <= MaxJpegMarkers)
        {
            if (!TryReadJpegMarker(data, ref offset, out var marker))
                return false;

            if (marker == 0xD9)
                return sawFrame && sawScan && offset == data.Length &&
                    CreateInfo(ImageAssetFormat.Jpeg, width, height, data.Length, out info);
            if (marker is 0xD8 or >= 0xD0 and <= 0xD7)
                return false;
            if (marker == 0x01)
                continue;
            if (!TryReadJpegSegment(data, ref offset, out var segment))
                return false;

            if (IsStartOfFrame(marker))
            {
                if (!TryReadJpegFrame(segment, out width, out height))
                    return false;
                sawFrame = true;
                continue;
            }

            if (marker != 0xDA)
                continue;
            if (!sawFrame || !IsValidJpegScan(segment))
                return false;
            sawScan = true;
            if (!TrySkipJpegEntropyData(data, ref offset, out var reachedEnd))
                return false;
            if (reachedEnd)
                return CreateInfo(ImageAssetFormat.Jpeg, width, height, data.Length, out info);
        }

        return false;
    }

    private static bool TryReadJpegMarker(ReadOnlySpan<byte> data, ref int offset, out byte marker)
    {
        marker = 0;
        if (offset >= data.Length || data[offset++] != 0xFF)
            return false;
        while (offset < data.Length && data[offset] == 0xFF)
            offset++;
        if (offset >= data.Length || data[offset] == 0x00)
            return false;

        marker = data[offset++];
        return true;
    }

    private static bool TryReadJpegSegment(ReadOnlySpan<byte> data, ref int offset, out ReadOnlySpan<byte> segment)
    {
        segment = default;
        if (data.Length - offset < 2)
            return false;
        var length = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
        if (length < 2 || length > data.Length - offset)
            return false;

        segment = data.Slice(offset + 2, length - 2);
        offset += length;
        return true;
    }

    private static bool TryReadJpegFrame(ReadOnlySpan<byte> segment, out uint width, out uint height)
    {
        width = 0;
        height = 0;
        if (segment.Length < 6 || segment[0] is not (8 or 12))
            return false;

        var components = segment[5];
        if (components is 0 or > 4 || segment.Length != 6 + 3 * components)
            return false;

        var componentIds = new HashSet<byte>();
        for (var component = 0; component < components; component++)
        {
            var componentOffset = 6 + component * 3;
            var sampling = segment[componentOffset + 1];
            if (!componentIds.Add(segment[componentOffset]) || (sampling >> 4) == 0 || (sampling & 0x0F) == 0)
                return false;
        }

        height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
        width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
        return IsWithinPixelBudget(width, height);
    }

    private static bool IsValidJpegScan(ReadOnlySpan<byte> segment)
    {
        if (segment.Length < 4)
            return false;
        var components = segment[0];
        return components is > 0 and <= 4 && segment.Length == 1 + 2 * components + 3;
    }

    private static bool TrySkipJpegEntropyData(ReadOnlySpan<byte> data, ref int offset, out bool reachedEnd)
    {
        reachedEnd = false;
        while (offset < data.Length)
        {
            if (data[offset++] != 0xFF)
                continue;
            if (offset >= data.Length)
                return false;

            var marker = data[offset++];
            while (marker == 0xFF)
            {
                if (offset >= data.Length)
                    return false;
                marker = data[offset++];
            }
            if (marker == 0x00 || marker is >= 0xD0 and <= 0xD7)
                continue;
            if (marker == 0xD9)
            {
                reachedEnd = offset == data.Length;
                return reachedEnd;
            }

            offset -= 2;
            return true;
        }
        return false;
    }

    private static bool TryReadGif(ReadOnlySpan<byte> data, out ImageAssetInfo? info)
    {
        info = null;
        if (data.Length < 14 ||
            (!data[..6].SequenceEqual("GIF87a"u8) && !data[..6].SequenceEqual("GIF89a"u8)))
            return false;

        var width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        if (!IsWithinPixelBudget(width, height))
            return false;

        var offset = 13;
        var packed = data[10];
        if ((packed & 0x80) != 0 && !TrySkipGifColorTable(data, ref offset, packed))
            return false;

        var frames = 0;
        long totalPixels = 0;
        var blocks = 0;
        while (offset < data.Length && ++blocks <= MaxGifBlocks)
        {
            switch (data[offset++])
            {
                case 0x3B:
                    return frames > 0 && offset == data.Length &&
                        CreateInfo(ImageAssetFormat.Gif, width, height, data.Length, out info);
                case 0x21:
                    if (offset >= data.Length)
                        return false;
                    offset++; // extension label
                    if (!TrySkipGifSubBlocks(data, ref offset, ref blocks, requireData: false))
                        return false;
                    break;
                case 0x2C:
                    if (!TryReadGifFrame(data, ref offset, width, height, ref totalPixels, ref frames, ref blocks))
                        return false;
                    break;
                default:
                    return false;
            }
        }

        return false;
    }

    private static bool TrySkipGifColorTable(ReadOnlySpan<byte> data, ref int offset, byte packed)
    {
        var entries = 1 << ((packed & 0x07) + 1);
        var bytes = 3 * entries;
        if (data.Length - offset < bytes)
            return false;
        offset += bytes;
        return true;
    }

    private static bool TryReadGifFrame(
        ReadOnlySpan<byte> data,
        ref int offset,
        int logicalWidth,
        int logicalHeight,
        ref long totalPixels,
        ref int frames,
        ref int blocks)
    {
        if (data.Length - offset < 9 || ++frames > MaxGifFrames)
            return false;

        var left = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        var top = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 2)..]);
        var width = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 4)..]);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 6)..]);
        var packed = data[offset + 8];
        offset += 9;
        if (width == 0 || height == 0 || left + width > logicalWidth || top + height > logicalHeight)
            return false;
        totalPixels += (long)width * height;
        if (totalPixels > MaxPixels)
            return false;
        if ((packed & 0x80) != 0 && !TrySkipGifColorTable(data, ref offset, packed))
            return false;
        if (offset >= data.Length || data[offset++] is < 2 or > 8)
            return false;

        return TrySkipGifSubBlocks(data, ref offset, ref blocks, requireData: true);
    }

    private static bool TrySkipGifSubBlocks(ReadOnlySpan<byte> data, ref int offset, ref int blocks, bool requireData)
    {
        var sawData = false;
        while (offset < data.Length && ++blocks <= MaxGifBlocks)
        {
            var length = data[offset++];
            if (length == 0)
                return !requireData || sawData;
            if (data.Length - offset < length)
                return false;
            sawData = true;
            offset += length;
        }
        return false;
    }

    private static bool TryReadBmp(ReadOnlySpan<byte> data, out ImageAssetInfo? info)
    {
        info = null;
        if (data.Length < 54 || data[0] != (byte)'B' || data[1] != (byte)'M')
            return false;

        var fileLength = BinaryPrimitives.ReadUInt32LittleEndian(data[2..]);
        var pixelOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[10..]);
        var dibLength = BinaryPrimitives.ReadUInt32LittleEndian(data[14..]);
        if (fileLength != data.Length || dibLength < 40 || dibLength > data.Length - 14 || pixelOffset > data.Length)
            return false;

        var width = BinaryPrimitives.ReadInt32LittleEndian(data[18..]);
        var signedHeight = BinaryPrimitives.ReadInt32LittleEndian(data[22..]);
        var planes = BinaryPrimitives.ReadUInt16LittleEndian(data[26..]);
        var bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(data[28..]);
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(data[30..]);
        var declaredImageSize = BinaryPrimitives.ReadUInt32LittleEndian(data[34..]);
        var colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(data[46..]);
        if (width <= 0 || signedHeight is 0 or int.MinValue || planes != 1 || compression != 0 ||
            bitsPerPixel is not (1 or 4 or 8 or 16 or 24 or 32))
            return false;

        var height = Math.Abs((long)signedHeight);
        if (!IsWithinPixelBudget((uint)width, (uint)height))
            return false;

        try
        {
            var paletteEntries = bitsPerPixel <= 8
                ? colorsUsed == 0 ? 1u << bitsPerPixel : colorsUsed
                : 0u;
            if (paletteEntries > (1u << bitsPerPixel))
                return false;
            var headerEnd = checked(14L + dibLength + 4L * paletteEntries);
            if (pixelOffset < headerEnd)
                return false;

            var stride = checked((((long)width * bitsPerPixel + 31) / 32) * 4);
            var pixelBytes = checked(stride * height);
            if (declaredImageSize != 0 && declaredImageSize != pixelBytes)
                return false;
            if ((long)pixelOffset + pixelBytes != data.Length)
                return false;

            return CreateInfo(ImageAssetFormat.Bmp, (uint)width, (uint)height, data.Length, out info);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool CreateInfo(
        ImageAssetFormat format,
        uint width,
        uint height,
        int byteLength,
        out ImageAssetInfo? info)
    {
        info = null;
        if (!IsWithinPixelBudget(width, height))
            return false;

        info = new ImageAssetInfo(format, (int)width, (int)height, byteLength);
        return true;
    }

    private static bool IsWithinPixelBudget(uint width, uint height) =>
        width > 0 && height > 0 && width <= MaxDimension && height <= MaxDimension &&
        (long)width * height <= MaxPixels;

    private static uint ComputeCrc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> payload)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var value in type)
            crc = UpdateCrc32(crc, value);
        foreach (var value in payload)
            crc = UpdateCrc32(crc, value);
        return ~crc;
    }

    private static uint UpdateCrc32(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
            crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        return crc;
    }

    private static bool IsStartOfFrame(byte marker) => marker is
        0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or
        0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;
}
