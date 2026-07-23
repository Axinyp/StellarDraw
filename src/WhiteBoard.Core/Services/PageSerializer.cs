using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;
using System.Text.Json;
using WhiteBoard.Contracts.Services;
using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;
using WhiteBoard.Protocol.Operations;
using WhiteBoard.Protocol.Serialization;

namespace WhiteBoard.Core.Services;

/// <summary>
/// 读写 page.bin 文件（通用对象块格式）。
/// Magic: "WBPG", 块结构: ChunkLength + ObjectKind + SchemaVersion + ObjectId +
///        ZIndex + Bounds(4×f32) + Transform(6×f32) + IsLocked + Reserved(3) + Payload + CRC32。
/// CRC32 校验失败的块跳过而不中断。
/// </summary>
public static class PageSerializer
{
    private static readonly byte[] Magic = "WBPG"u8.ToArray();
    private const int HeaderSize = 22;   // Magic(4)+Version(2)+Flags(4)+Count(4)+CRC(4)+Reserved(4)
    private const int ChunkFixedHdr = 4 + 2 + 2 + 16 + 4 + 16 + 24 + 1 + 3; // = 72 bytes before payload
    private const int StrokePayloadHeaderSize = 16;
    private const int BytesPerStrokePoint = 12;
    private const int MaxStrokePointCount = 1_000_000;
    private const int MaxTextContentBytes = 1 * 1024 * 1024;
    private const int MaxTextStyleBytes = 256 * 1024;
    private const int MaxJsonPayloadBytes = 4 * 1024 * 1024;
    private const int MaxDocumentReferenceBytes = 4 * 1024;
    private const int MaxPathDataBytes = 1 * 1024 * 1024;
    private const int MaxObjectCount = 50_000;

    /// <summary>
    /// 克隆 <see cref="SerializeObject"/> 产生的字节块，赋予新 Guid 并对 Bounds/Transform 施加平移偏移。
    /// 可直接传给 <see cref="DeserializeObject"/> 得到独立的副本对象。
    /// </summary>
    public static byte[] CloneObjectWithOffset(byte[] data, float dx, float dy)
    {
        // 最短校验（72 字节固定头 + 4 ChunkLen + 4 CRC）
        if (data.Length < 80) return data;

        var buf = (byte[])data.Clone();
        var chunkLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(buf);
        if (chunkLen != buf.Length || chunkLen < 12) return data;

        // Buffer layout (offset from buf[0]):
        //  0-3   : ChunkLength
        //  4-5   : kind, 6-7: schemaVer, 8-23: ObjectId
        //  24-27 : ZIndex, 28-31: BoundsX, 32-35: BoundsY
        //  36-39 : BoundsW, 40-43: BoundsH
        //  44-67 : Transform[0..5]  (T[4]=OffX at 60, T[5]=OffY at 64)
        //  68    : IsLocked, 69-71: Reserved, 72+: payload
        //  last 4: CRC32

        // 新 Guid
        Guid.NewGuid().ToByteArray().CopyTo(buf, 8);

        // BoundsX / BoundsY
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(28), BitConverter.ToSingle(buf, 28) + dx);
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(32), BitConverter.ToSingle(buf, 32) + dy);

        // Transform[4] = OffsetX, Transform[5] = OffsetY
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(60), BitConverter.ToSingle(buf, 60) + dx);
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(64), BitConverter.ToSingle(buf, 64) + dy);

        // 重新计算 CRC：chunkBody = buf[4..(chunkLen-4)]
        var crc = Crc32.HashToUInt32(buf.AsSpan(4, chunkLen - 8));
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(chunkLen - 4), crc);

        return buf;
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public static byte[] Serialize(PageModel page)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        var objects = page.GetAllObjects();

        // Header placeholder (we'll fill CRC + count after writing chunks)
        long headerPos = ms.Position;
        bw.Write(Magic);                                         // 4
        bw.Write((ushort)ProtocolVersion.CurrentPageBin);       // 2
        bw.Write((uint)0);                                       // FeatureFlags 4
        bw.Write((uint)objects.Count);                           // ObjectCount 4
        bw.Write((uint)0);                                       // HeaderCRC32 (placeholder) 4
        bw.Write((uint)0);                                       // Reserved 4
        bw.Flush();

        // Chunks
        foreach (var obj in objects)
            WriteChunk(bw, obj);

        bw.Flush();

        // Fill HeaderCRC32
        var buf = ms.ToArray();
        var headerCrc = Crc32.HashToUInt32(buf.AsSpan(0, HeaderSize - 8)); // exclude the CRC field itself
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan((int)headerPos + 14), headerCrc);
        return buf;
    }

    /// <summary>
    /// 将单个对象序列化为独立字节块，供跨端协同事件（如 StrokeAddedEvent.StrokeData）使用。
    /// 格式与 Serialize 中的 Chunk 完全一致，可通过 DeserializeObject 还原。
    /// </summary>
    public static byte[] SerializeObject(WhiteboardObject obj)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        WriteChunk(bw, obj);
        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// 从 SerializeObject 产生的字节反序列化单个对象。
    /// CRC 校验失败、格式错误或数据不足时返回 null，不抛异常。
    /// </summary>
    public static WhiteboardObject? DeserializeObject(byte[] data)
    {
        if (data.Length < 8) return null;
        var span = data.AsSpan();

        var chunkLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
        if (chunkLength != data.Length || chunkLength < 8) return null;

        var chunkBody = span[4..(chunkLength - 4)];
        var storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(span[(chunkLength - 4)..]);
        var actualCrc = Crc32.HashToUInt32(chunkBody);
        if (actualCrc != storedCrc) return null;

        try { return ReadChunk(chunkBody); }
        catch { return null; }
    }

    private static void WriteChunk(BinaryWriter bw, WhiteboardObject obj)
    {
        using var chunkMs = new MemoryStream();
        using var cbw = new BinaryWriter(chunkMs, Encoding.UTF8, leaveOpen: true);

        var kind = GetKind(obj);
        var payload = SerializePayload(obj);

        cbw.Write((ushort)kind);                   // ObjectKind  2
        cbw.Write((ushort)1);                      // SchemaVersion 2
        cbw.Write(obj.Id.ToByteArray());           // ObjectId    16
        cbw.Write(obj.ZIndex);                     // ZIndex      4
        cbw.Write(obj.BoundsX);                    // Bounds X    4
        cbw.Write(obj.BoundsY);                    // Bounds Y    4
        cbw.Write(obj.BoundsWidth);                // Bounds W    4
        cbw.Write(obj.BoundsHeight);               // Bounds H    4
        foreach (var f in obj.Transform) cbw.Write(f); // Transform 24
        cbw.Write(obj.IsLocked ? (byte)1 : (byte)0);  // IsLocked 1
        cbw.Write((byte)0); cbw.Write((byte)0); cbw.Write((byte)0); // Reserved 3
        cbw.Write(payload);
        cbw.Flush();

        var chunkBody = chunkMs.ToArray();
        var chunkCrc = Crc32.HashToUInt32(chunkBody);

        bw.Write((uint)(chunkBody.Length + 8)); // ChunkLength = body + CRC(4) + Length field itself(4)
        bw.Write(chunkBody);
        bw.Write(chunkCrc);
    }

    private static byte[] SerializePayload(WhiteboardObject obj) => obj switch
    {
        StrokeObject s => SerializeStroke(s),
        ImageObject img => SerializeImage(img),
        TextObject t => SerializeText(t),
        ShapeObject sh => SerializeShape(sh),
        PathObject p => SerializePath(p),
        MindMapObject mm => SerializeJson(mm.Data.ToJson()),
        TableObject tb => SerializeJson(tb.Data.ToJson()),
        DocumentObject d => SerializeDocument(d),
        _ => [],
    };

    private static byte[] SerializeStroke(StrokeObject s)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        uint styleFlags = (uint)s.Style.PenType | ((uint)s.Style.InkType << 4)
            | (s.Style.IsFilled ? 1u << 8 : 0u)
            | (s.Style.IsAntiAlias ? 1u << 9 : 0u);
        bw.Write(styleFlags);
        bw.Write(s.Style.ColorArgb);
        bw.Write(s.Style.Thickness);
        bw.Write((uint)s.Points.Count);
        foreach (var p in s.Points) { bw.Write(p.X); bw.Write(p.Y); bw.Write(p.Pressure); }
        return ms.ToArray();
    }

    private static byte[] SerializeImage(ImageObject img)
    {
        var idBytes = Convert.FromHexString(img.AssetId.PadRight(32, '0')[..32]);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(idBytes);
        bw.Write(img.OriginalWidth);
        bw.Write(img.OriginalHeight);
        return ms.ToArray();
    }

    private static byte[] SerializeText(TextObject t)
    {
        var contentBytes = Encoding.UTF8.GetBytes(t.Content);
        var styleJson = JsonSerializer.SerializeToUtf8Bytes(t.Style);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((uint)contentBytes.Length);
        bw.Write(contentBytes);
        bw.Write((uint)styleJson.Length);
        bw.Write(styleJson);
        return ms.ToArray();
    }

    private static byte[] SerializeJson(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((uint)bytes.Length);
        bw.Write(bytes);
        return ms.ToArray();
    }

    private static byte[] SerializeDocument(DocumentObject d)
    {
        var refBytes = Encoding.UTF8.GetBytes(d.FileRef);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((uint)refBytes.Length);
        bw.Write(refBytes);
        bw.Write(d.PageCount);
        return ms.ToArray();
    }

    private static byte[] SerializeShape(ShapeObject sh)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((byte)sh.Type);
        bw.Write(sh.FillArgb);
        bw.Write(sh.StrokeArgb);
        bw.Write(sh.StrokeWidth);
        bw.Write(sh.IsFilled ? (byte)1 : (byte)0);
        return ms.ToArray();
    }

    private static byte[] SerializePath(PathObject p)
    {
        var pathBytes = Encoding.UTF8.GetBytes(p.PathData);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((uint)pathBytes.Length);
        bw.Write(pathBytes);
        bw.Write(p.FillArgb);
        bw.Write(p.StrokeArgb);
        bw.Write(p.StrokeWidth);
        bw.Write(p.IsFilled ? (byte)1 : (byte)0);
        return ms.ToArray();
    }

    private static ObjectKind GetKind(WhiteboardObject obj) => obj switch
    {
        StrokeObject => ObjectKind.Stroke,
        ImageObject => ObjectKind.Image,
        TextObject => ObjectKind.Text,
        ShapeObject => ObjectKind.Shape,
        PathObject => ObjectKind.Path,
        MindMapObject => ObjectKind.MindMap,
        TableObject => ObjectKind.Table,
        DocumentObject => ObjectKind.Document,
        _ => throw new NotSupportedException($"Unknown object type: {obj.GetType().Name}"),
    };

    // ── Read ──────────────────────────────────────────────────────────────────

    public static PageModel Deserialize(byte[] data, string pageId, string title = "Page")
    {
        var page = new PageModel(pageId, title);
        if (data.Length < HeaderSize) return page;

        var span = data.AsSpan();

        // Validate magic
        if (!span[..4].SequenceEqual(Magic)) return page;

        var version = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]);
        if (version < ProtocolVersion.MinSupportedPageBin) return page;

        var objectCount = BinaryPrimitives.ReadUInt32LittleEndian(span[10..]);
        if (objectCount > MaxObjectCount)
            throw new InvalidDataException("页面对象数量超过限制。");

        int pos = HeaderSize;

        for (uint i = 0; i < objectCount && pos + 8 <= data.Length; i++)
        {
            var chunkLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[pos..]);
            // 前置校验：避免畸形长度在 try/catch 之外引发 ArgumentOutOfRange
            if (chunkLength < 8 || pos > data.Length - chunkLength) break;

            var chunkBody = span[(pos + 4)..(pos + chunkLength - 4)];
            var storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(span[(pos + chunkLength - 4)..]);
            var actualCrc = Crc32.HashToUInt32(chunkBody);

            if (actualCrc != storedCrc)
            {
                pos += chunkLength;
                continue; // 校验失败，跳过损坏块
            }

            try
            {
                var obj = ReadChunk(chunkBody);
                if (obj != null) page.AddObject(obj);
            }
            catch
            {
                // 单块解析失败不中断整体加载
            }

            pos += chunkLength;
        }

        return page;
    }

    private static WhiteboardObject? ReadChunk(ReadOnlySpan<byte> body)
    {
        if (body.Length < ChunkFixedHdr) return null;

        var kind = (ObjectKind)BinaryPrimitives.ReadUInt16LittleEndian(body[0..]);
        // schemaVersion = body[2..4] (reserved for future migration)
        var id = new Guid(body[4..20]);
        var zIndex = BinaryPrimitives.ReadInt32LittleEndian(body[20..]);
        var bx = BitConverter.ToSingle(body[24..]);
        var by = BitConverter.ToSingle(body[28..]);
        var bw = BitConverter.ToSingle(body[32..]);
        var bh = BitConverter.ToSingle(body[36..]);
        float[] transform = new float[6];
        for (int i = 0; i < 6; i++)
            transform[i] = BitConverter.ToSingle(body[(40 + i * 4)..]);
        var isLocked = body[64] != 0;
        var payload = body[68..];

        if (!IsFiniteGeometry(bx, by, bw, bh, transform))
            throw new InvalidDataException("对象几何数据无效。");

        var obj = DeserializePayload(kind, id, bx, by, bw, bh, transform, payload);
        if (obj != null)
        {
            obj.ZIndex = zIndex;
            obj.IsLocked = isLocked;
        }
        return obj;
    }

    private static WhiteboardObject? DeserializePayload(ObjectKind kind, Guid id,
        float bx, float by, float bw, float bh, float[] transform, ReadOnlySpan<byte> payload)
        => kind switch
        {
            ObjectKind.Stroke => DeserializeStroke(id, bx, by, bw, bh, transform, payload),
            ObjectKind.Image => DeserializeImage(id, bx, by, bw, bh, transform, payload),
            ObjectKind.Text => DeserializeText(id, bx, by, bw, bh, transform, payload),
            ObjectKind.Shape => DeserializeShape(id, bx, by, bw, bh, transform, payload),
            ObjectKind.Path => DeserializePath(id, bx, by, bw, bh, transform, payload),
            ObjectKind.MindMap => DeserializeMindMap(id, bx, by, bw, bh, transform, payload),
            ObjectKind.Table => DeserializeTable(id, bx, by, bw, bh, transform, payload),
            ObjectKind.Document => DeserializeDocument(id, bx, by, bw, bh, transform, payload),
            _ => null,
        };

    private static bool IsFiniteGeometry(float x, float y, float width, float height, float[] transform) =>
        WhiteboardGeometry.HasValidBounds(x, y, width, height) &&
        transform.All(float.IsFinite);

    private static StrokeObject DeserializeStroke(Guid id, float bx, float by, float bw, float bh,
        float[] transform, ReadOnlySpan<byte> payload)
    {
        if (payload.Length < StrokePayloadHeaderSize)
            throw new InvalidDataException("笔画数据截断。");

        var styleFlags = BinaryPrimitives.ReadUInt32LittleEndian(payload[0..]);
        var colorArgb = BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]);
        var thickness = BitConverter.ToSingle(payload[8..]);
        var pointCount = BinaryPrimitives.ReadUInt32LittleEndian(payload[12..]);
        var availablePointCount = (payload.Length - StrokePayloadHeaderSize) / BytesPerStrokePoint;
        if (pointCount > MaxStrokePointCount || pointCount > availablePointCount ||
            payload.Length != StrokePayloadHeaderSize + (long)pointCount * BytesPerStrokePoint)
            throw new InvalidDataException("笔画点数据无效。");

        var points = new PressurePoint[(int)pointCount];
        for (int i = 0; i < points.Length; i++)
        {
            int offset = StrokePayloadHeaderSize + i * BytesPerStrokePoint;
            points[i] = new PressurePoint(
                BitConverter.ToSingle(payload[offset..]),
                BitConverter.ToSingle(payload[(offset + 4)..]),
                BitConverter.ToSingle(payload[(offset + 8)..]),
                0L);
        }
        var style = new StrokeStyle(
            (PenType)(styleFlags & 0xF),
            (InkType)((styleFlags >> 4) & 0xF),
            colorArgb, thickness,
            (styleFlags & (1u << 8)) != 0,
            (styleFlags & (1u << 9)) != 0);
        if (!Enum.IsDefined(style.PenType) || !Enum.IsDefined(style.InkType) ||
            !float.IsFinite(style.Thickness) || style.Thickness <= 0 ||
            points.Any(point => !float.IsFinite(point.X) || !float.IsFinite(point.Y) ||
                !float.IsFinite(point.Pressure)))
            throw new InvalidDataException("笔画样式或坐标无效。");

        return new StrokeObject(id, points, style, bx, by, bw, bh, transform);
    }

    private static ImageObject DeserializeImage(Guid id, float bx, float by, float bw, float bh,
        float[] transform, ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 24)
            throw new InvalidDataException("图片数据无效。");

        var assetId = Convert.ToHexString(payload[..16].ToArray()).ToLowerInvariant();
        var origW = BitConverter.ToSingle(payload[16..]);
        var origH = BitConverter.ToSingle(payload[20..]);
        if (!float.IsFinite(origW) || origW <= 0 || !float.IsFinite(origH) || origH <= 0)
            throw new InvalidDataException("图片尺寸无效。");

        return new ImageObject(id, assetId, origW, origH, bx, by, bw, bh, transform);
    }

    private static TextObject DeserializeText(Guid id, float bx, float by, float bw, float bh,
        float[] transform, ReadOnlySpan<byte> payload)
    {
        var offset = 0;
        var contentBytes = ReadLengthPrefixedBytes(
            payload, ref offset, MaxTextContentBytes, "文本内容");
        var styleJson = ReadLengthPrefixedBytes(
            payload, ref offset, MaxTextStyleBytes, "文本样式");
        if (offset != payload.Length)
            throw new InvalidDataException("文本数据包含多余字节。");

        var content = Encoding.UTF8.GetString(contentBytes);
        var style = JsonSerializer.Deserialize<TextStyle>(styleJson) ??
            throw new InvalidDataException("文本样式无效。");
        if (string.IsNullOrWhiteSpace(style.FontFamily) || !float.IsFinite(style.FontSize) || style.FontSize <= 0)
            throw new InvalidDataException("文本样式无效。");

        return new TextObject(id, content, style, bx, by, bw, bh, transform);
    }

    private static MindMapObject DeserializeMindMap(Guid id, float bx, float by, float bw, float bh,
        float[] transform, ReadOnlySpan<byte> payload)
    {
        var offset = 0;
        var jsonBytes = ReadLengthPrefixedBytes(payload, ref offset, MaxJsonPayloadBytes, "思维导图");
        if (offset != payload.Length)
            throw new InvalidDataException("思维导图数据包含多余字节。");

        var data = MindMapData.FromJson(Encoding.UTF8.GetString(jsonBytes));
        return new MindMapObject(id, data, bx, by, bw, bh, transform);
    }

    private static TableObject DeserializeTable(Guid id, float bx, float by, float bw, float bh,
        float[] transform, ReadOnlySpan<byte> payload)
    {
        var offset = 0;
        var jsonBytes = ReadLengthPrefixedBytes(payload, ref offset, MaxJsonPayloadBytes, "表格");
        if (offset != payload.Length)
            throw new InvalidDataException("表格数据包含多余字节。");

        var data = TableData.FromJson(Encoding.UTF8.GetString(jsonBytes));
        return new TableObject(id, data, bx, by, bw, bh, transform);
    }

    private static DocumentObject DeserializeDocument(Guid id, float bx, float by, float bw, float bh,
        float[] transform, ReadOnlySpan<byte> payload)
    {
        var offset = 0;
        var fileRefBytes = ReadLengthPrefixedBytes(
            payload, ref offset, MaxDocumentReferenceBytes, "文档引用");
        if (payload.Length - offset != sizeof(int))
            throw new InvalidDataException("文档数据无效。");

        var pageCount = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
        return new DocumentObject(id, Encoding.UTF8.GetString(fileRefBytes), pageCount, bx, by, bw, bh, transform);
    }

    private static ShapeObject DeserializeShape(Guid id, float bx, float by, float bw, float bh,
        float[] transform, ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 14)
            throw new InvalidDataException("形状数据无效。");

        var type = (ShapeType)payload[0];
        var fillArgb = BinaryPrimitives.ReadUInt32LittleEndian(payload[1..]);
        var strokeArgb = BinaryPrimitives.ReadUInt32LittleEndian(payload[5..]);
        var strokeWidth = BitConverter.ToSingle(payload[9..]);
        var isFilled = payload[13] != 0;
        if (!Enum.IsDefined(type) || !float.IsFinite(strokeWidth) || strokeWidth <= 0)
            throw new InvalidDataException("形状属性无效。");

        return new ShapeObject(id, type, fillArgb, strokeArgb, strokeWidth, isFilled, bx, by, bw, bh, transform);
    }

    private static PathObject DeserializePath(Guid id, float bx, float by, float bw, float bh,
        float[] transform, ReadOnlySpan<byte> payload)
    {
        var offset = 0;
        var pathBytes = ReadLengthPrefixedBytes(payload, ref offset, MaxPathDataBytes, "路径");
        const int tailLength = sizeof(uint) + sizeof(uint) + sizeof(float) + sizeof(byte);
        if (payload.Length - offset != tailLength)
            throw new InvalidDataException("路径数据无效。");

        var pathData = Encoding.UTF8.GetString(pathBytes);
        var fillArgb = BinaryPrimitives.ReadUInt32LittleEndian(payload[offset..]);
        var strokeArgb = BinaryPrimitives.ReadUInt32LittleEndian(payload[(offset + 4)..]);
        var strokeWidth = BitConverter.ToSingle(payload[(offset + 8)..]);
        var isFilled = payload[offset + 12] != 0;
        if (!float.IsFinite(strokeWidth) || strokeWidth <= 0)
            throw new InvalidDataException("路径描边无效。");

        return new PathObject(id, pathData, fillArgb, strokeArgb, strokeWidth, isFilled, bx, by, bw, bh, transform);
    }

    private static ReadOnlySpan<byte> ReadLengthPrefixedBytes(
        ReadOnlySpan<byte> payload,
        ref int offset,
        int maximumLength,
        string fieldName)
    {
        if (payload.Length - offset < sizeof(uint))
            throw new InvalidDataException($"{fieldName}数据截断。");

        var length = BinaryPrimitives.ReadUInt32LittleEndian(payload[offset..]);
        offset += sizeof(uint);
        if (length > maximumLength || length > payload.Length - offset)
            throw new InvalidDataException($"{fieldName}长度无效。");

        var result = payload.Slice(offset, (int)length);
        offset += (int)length;
        return result;
    }
}