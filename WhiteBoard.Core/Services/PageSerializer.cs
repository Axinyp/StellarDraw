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
    private const int HeaderSize   = 22;   // Magic(4)+Version(2)+Flags(4)+Count(4)+CRC(4)+Reserved(4)
    private const int ChunkFixedHdr = 4+2+2+16+4+16+24+1+3; // = 72 bytes before payload

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

        try   { return ReadChunk(chunkBody); }
        catch { return null; }
    }

    private static void WriteChunk(BinaryWriter bw, WhiteboardObject obj)
    {
        using var chunkMs = new MemoryStream();
        using var cbw     = new BinaryWriter(chunkMs, Encoding.UTF8, leaveOpen: true);

        var kind    = GetKind(obj);
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
        var chunkCrc  = Crc32.HashToUInt32(chunkBody);

        bw.Write((uint)(chunkBody.Length + 8)); // ChunkLength = body + CRC(4) + Length field itself(4)
        bw.Write(chunkBody);
        bw.Write(chunkCrc);
    }

    private static byte[] SerializePayload(WhiteboardObject obj) => obj switch
    {
        StrokeObject  s   => SerializeStroke(s),
        ImageObject   img => SerializeImage(img),
        TextObject    t   => SerializeText(t),
        ShapeObject   sh  => SerializeShape(sh),
        PathObject    p   => SerializePath(p),
        MindMapObject mm  => SerializeJson(mm.Data.ToJson()),
        TableObject   tb  => SerializeJson(tb.Data.ToJson()),
        DocumentObject d  => SerializeDocument(d),
        _                 => [],
    };

    private static byte[] SerializeStroke(StrokeObject s)
    {
        using var ms  = new MemoryStream();
        using var bw  = new BinaryWriter(ms);
        uint styleFlags = (uint)s.Style.PenType | ((uint)s.Style.InkType << 4)
            | (s.Style.IsFilled    ? 1u << 8 : 0u)
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
        var styleJson    = JsonSerializer.SerializeToUtf8Bytes(t.Style);
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
        StrokeObject   => ObjectKind.Stroke,
        ImageObject    => ObjectKind.Image,
        TextObject     => ObjectKind.Text,
        ShapeObject    => ObjectKind.Shape,
        PathObject     => ObjectKind.Path,
        MindMapObject  => ObjectKind.MindMap,
        TableObject    => ObjectKind.Table,
        DocumentObject => ObjectKind.Document,
        _              => throw new NotSupportedException($"Unknown object type: {obj.GetType().Name}"),
    };

    // ── Read ──────────────────────────────────────────────────────────────────

    public static PageModel Deserialize(byte[] data, string pageId, string title = "Page")
    {
        var page = new PageModel(pageId, title);
        if (data.Length < HeaderSize) return page;

        var span = data.AsSpan();

        // Validate magic
        if (!span[..4].SequenceEqual(Magic)) return page;

        var version    = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]);
        if (version < ProtocolVersion.MinSupportedPageBin) return page;

        var objectCount = BinaryPrimitives.ReadUInt32LittleEndian(span[10..]);
        int pos         = HeaderSize;

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

        var kind    = (ObjectKind)BinaryPrimitives.ReadUInt16LittleEndian(body[0..]);
        // schemaVersion = body[2..4] (reserved for future migration)
        var id      = new Guid(body[4..20]);
        var zIndex  = BinaryPrimitives.ReadInt32LittleEndian(body[20..]);
        var bx      = BitConverter.ToSingle(body[24..]);
        var by      = BitConverter.ToSingle(body[28..]);
        var bw      = BitConverter.ToSingle(body[32..]);
        var bh      = BitConverter.ToSingle(body[36..]);
        float[] transform = new float[6];
        for (int i = 0; i < 6; i++)
            transform[i] = BitConverter.ToSingle(body[(40 + i * 4)..]);
        var isLocked = body[64] != 0;
        var payload  = body[68..];

        var obj = DeserializePayload(kind, id, bx, by, bw, bh, transform, payload);
        if (obj != null)
        {
            obj.ZIndex   = zIndex;
            obj.IsLocked = isLocked;
        }
        return obj;
    }

    private static WhiteboardObject? DeserializePayload(ObjectKind kind, Guid id,
        float bx, float by, float bw, float bh, float[] transform, ReadOnlySpan<byte> payload)
        => kind switch
        {
            ObjectKind.Stroke   => DeserializeStroke(id, bx, by, bw, bh, transform, payload),
            ObjectKind.Image    => DeserializeImage(id, bx, by, bw, bh, transform, payload),
            ObjectKind.Text     => DeserializeText(id, bx, by, bw, bh, transform, payload),
            ObjectKind.Shape    => DeserializeShape(id, bx, by, bw, bh, transform, payload),
            ObjectKind.Path     => DeserializePath(id, bx, by, bw, bh, transform, payload),
            ObjectKind.MindMap  => DeserializeMindMap(id, bx, by, bw, bh, transform, payload),
            ObjectKind.Table    => DeserializeTable(id, bx, by, bw, bh, transform, payload),
            ObjectKind.Document => DeserializeDocument(id, bx, by, bw, bh, transform, payload),
            _                   => null,
        };

    private static StrokeObject DeserializeStroke(Guid id, float bx, float by, float bw, float bh,
        float[] transform, ReadOnlySpan<byte> payload)
    {
        var styleFlags  = BinaryPrimitives.ReadUInt32LittleEndian(payload[0..]);
        var colorArgb   = BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]);
        var thickness   = BitConverter.ToSingle(payload[8..]);
        var pointCount  = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload[12..]);
        var points      = new PressurePoint[pointCount];
        for (int i = 0; i < pointCount; i++)
        {
            int o = 16 + i * 12;
            points[i] = new PressurePoint(
                BitConverter.ToSingle(payload[o..]),
                BitConverter.ToSingle(payload[(o + 4)..]),
                BitConverter.ToSingle(payload[(o + 8)..]),
                0L);
        }
        var style = new StrokeStyle(
            (PenType)(styleFlags & 0xF),
            (InkType)((styleFlags >> 4) & 0xF),
            colorArgb, thickness,
            (styleFlags & (1u << 8)) != 0,
            (styleFlags & (1u << 9)) != 0);
        return new StrokeObject(id, points, style, bx, by, bw, bh, transform);
    }

    private static ImageObject DeserializeImage(Guid id, float bx, float by, float bw, float bh,
        float[] transform, ReadOnlySpan<byte> payload)
    {
        var assetId  = Convert.ToHexString(payload[..16].ToArray()).ToLowerInvariant();
        var origW    = BitConverter.ToSingle(payload[16..]);
        var origH    = BitConverter.ToSingle(payload[20..]);
        return new ImageObject(id, assetId, origW, origH, bx, by, bw, bh, transform);
    }

    private static TextObject DeserializeText(Guid id, float bx, float by, float bw, float bh,
        float[] transform, ReadOnlySpan<byte> payload)
    {
        var contentLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload[0..]);
        var content    = Encoding.UTF8.GetString(payload[4..(4 + contentLen)]);
        var styleLen   = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload[(4 + contentLen)..]);
        var styleJson  = payload[(8 + contentLen)..(8 + contentLen + styleLen)];
        var style      = JsonSerializer.Deserialize<TextStyle>(styleJson) ?? new TextStyle("Arial", 14, false, false, 0xFF000000);
        return new TextObject(id, content, style, bx, by, bw, bh, transform);
    }

    private static MindMapObject DeserializeMindMap(Guid id, float bx, float by, float bw, float bh,
        float[] transform, ReadOnlySpan<byte> payload)
    {
        var len  = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload[0..]);
        var json = Encoding.UTF8.GetString(payload[4..(4 + len)]);
        var data = MindMapData.FromJson(json);
        return new MindMapObject(id, data, bx, by, bw, bh, transform);
    }

    private static TableObject DeserializeTable(Guid id, float bx, float by, float bw, float bh,
        float[] transform, ReadOnlySpan<byte> payload)
    {
        var len  = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload[0..]);
        var json = Encoding.UTF8.GetString(payload[4..(4 + len)]);
        var data = TableData.FromJson(json);
        return new TableObject(id, data, bx, by, bw, bh, transform);
    }

    private static DocumentObject DeserializeDocument(Guid id, float bx, float by, float bw, float bh,
        float[] transform, ReadOnlySpan<byte> payload)
    {
        var refLen    = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload[0..]);
        var fileRef   = Encoding.UTF8.GetString(payload[4..(4 + refLen)]);
        var pageCount = BinaryPrimitives.ReadInt32LittleEndian(payload[(4 + refLen)..]);
        return new DocumentObject(id, fileRef, pageCount, bx, by, bw, bh, transform);
    }

    private static ShapeObject DeserializeShape(Guid id, float bx, float by, float bw, float bh,
        float[] transform, ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 14) return new ShapeObject(id, ShapeType.Rectangle, 0, 0xFF000000, 1f, false, bx, by, bw, bh, transform);
        var type        = (ShapeType)payload[0];
        var fillArgb    = BinaryPrimitives.ReadUInt32LittleEndian(payload[1..]);
        var strokeArgb  = BinaryPrimitives.ReadUInt32LittleEndian(payload[5..]);
        var strokeWidth = BitConverter.ToSingle(payload[9..]);
        var isFilled    = payload[13] != 0;
        return new ShapeObject(id, type, fillArgb, strokeArgb, strokeWidth, isFilled, bx, by, bw, bh, transform);
    }

    private static PathObject DeserializePath(Guid id, float bx, float by, float bw, float bh,
        float[] transform, ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
            return new PathObject(id, "", 0xFF000000, 0xFF000000, 1f, false, bx, by, bw, bh, transform);

        var pathLen  = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload[0..]);
        var minRequired = 4 + pathLen + 9;
        if (payload.Length < minRequired)
            return new PathObject(id, "", 0xFF000000, 0xFF000000, 1f, false, bx, by, bw, bh, transform);

        var pathData    = Encoding.UTF8.GetString(payload[4..(4 + pathLen)]);
        int tail        = 4 + pathLen;
        var fillArgb    = BinaryPrimitives.ReadUInt32LittleEndian(payload[tail..]);
        var strokeArgb  = BinaryPrimitives.ReadUInt32LittleEndian(payload[(tail + 4)..]);
        var strokeWidth = BitConverter.ToSingle(payload[(tail + 8)..]);
        var isFilled    = payload[tail + 12] != 0;

        return new PathObject(id, pathData, fillArgb, strokeArgb, strokeWidth, isFilled, bx, by, bw, bh, transform);
    }
}
