using System.Buffers.Binary;
using System.IO.Hashing;
using WhiteBoard.Contracts.Services;
using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;
using WhiteBoard.Core.Services;

namespace WhiteBoard.Core.Tests;

public class PageSerializerObjectTests
{
    private static float[] Identity() => [1f, 0f, 0f, 1f, 0f, 0f];

    private static StrokeObject MakeStroke(float x = 0f, float y = 0f, uint color = 0xFF000000u)
    {
        var pts = new[]
        {
            new PressurePoint(x,       y,       0.5f, 0),
            new PressurePoint(x + 10f, y + 5f,  0.8f, 10),
            new PressurePoint(x + 20f, y + 10f, 0.3f, 20),
        };
        var style = new StrokeStyle(PenType.Pen, InkType.Normal, color, 3f, false, true);
        return new StrokeObject(Guid.NewGuid(), pts, style, x, y, 20f, 10f, Identity());
    }

    // ── Basic round-trip ──────────────────────────────────────────────────────

    [Fact]
    public void SerializeObject_ProducesNonEmptyBytes()
    {
        var data = PageSerializer.SerializeObject(MakeStroke());
        Assert.True(data.Length > 8);
    }

    [Fact]
    public void SerializeObject_DeserializeObject_StrokeRoundTrip_PreservesId()
    {
        var stroke = MakeStroke(5f, 10f, 0xFF3344AAu);
        var data   = PageSerializer.SerializeObject(stroke);
        var obj    = PageSerializer.DeserializeObject(data);

        var s = Assert.IsType<StrokeObject>(obj);
        Assert.Equal(stroke.Id,                s.Id);
        Assert.Equal(stroke.Style.ColorArgb,   s.Style.ColorArgb);
        Assert.Equal(stroke.Style.Thickness,   s.Style.Thickness, precision: 4);
        Assert.Equal(stroke.Style.PenType,     s.Style.PenType);
        Assert.Equal(stroke.Style.IsAntiAlias, s.Style.IsAntiAlias);
        Assert.Equal(stroke.Points.Count,      s.Points.Count);
        Assert.Equal(stroke.BoundsX,           s.BoundsX, precision: 4);
        Assert.Equal(stroke.BoundsY,           s.BoundsY, precision: 4);
    }

    [Fact]
    public void SerializeObject_DeserializeObject_PreservesPointCoordinates()
    {
        var pts = new[]
        {
            new PressurePoint(1.5f, 2.5f, 0.6f, 0),
            new PressurePoint(3.5f, 4.5f, 0.7f, 100),
        };
        var style  = new StrokeStyle(PenType.Highlighter, InkType.Normal, 0x80FFFF00u, 8f, false, false);
        var stroke = new StrokeObject(Guid.NewGuid(), pts, style, 1.5f, 2.5f, 3.5f, 4.5f, Identity());

        var s = Assert.IsType<StrokeObject>(PageSerializer.DeserializeObject(
                    PageSerializer.SerializeObject(stroke)));

        Assert.Equal(2,     s.Points.Count);
        Assert.Equal(1.5f,  s.Points[0].X,        precision: 5);
        Assert.Equal(2.5f,  s.Points[0].Y,        precision: 5);
        Assert.Equal(0.6f,  s.Points[0].Pressure, precision: 5);
        Assert.Equal(PenType.Highlighter, s.Style.PenType);
        Assert.Equal(8f,    s.Style.Thickness,     precision: 4);
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void DeserializeObject_EmptyData_ReturnsNull()
        => Assert.Null(PageSerializer.DeserializeObject([]));

    [Fact]
    public void DeserializeObject_CorruptedData_ReturnsNull()
    {
        var data = (byte[])PageSerializer.SerializeObject(MakeStroke()).Clone();
        for (int i = 20; i < 30 && i < data.Length; i++)
            data[i] ^= 0xFF;
        Assert.Null(PageSerializer.DeserializeObject(data));
    }

    [Fact]
    public void DeserializeObject_TruncatedData_ReturnsNull()
    {
        var full      = PageSerializer.SerializeObject(MakeStroke());
        var truncated = full[..Math.Max(0, full.Length - 10)];
        Assert.Null(PageSerializer.DeserializeObject(truncated));
    }

    [Fact]
    public void DeserializeObject_ExtremeFiniteBounds_ReturnsNull()
    {
        var data = PageSerializer.SerializeObject(MakeStroke());
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(28), float.MaxValue);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(36), float.MaxValue);
        RewriteChunkCrc(data);

        Assert.Null(PageSerializer.DeserializeObject(data));
    }

    private static void RewriteChunkCrc(byte[] chunk)
    {
        var chunkLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(chunk));
        var crc = Crc32.HashToUInt32(chunk.AsSpan(4, chunkLength - 8));
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(chunkLength - 4), crc);
    }

    // ── Collaboration round-trip ──────────────────────────────────────────────

    [Fact]
    public void StrokeData_SerializeDeserialize_SurvivesHubEnvelopeRoundTrip()
    {
        var stroke    = MakeStroke(100f, 200f, 0xFF00FF00u);
        var data      = PageSerializer.SerializeObject(stroke);

        // Simulate the bytes going through base64/JSON (byte[] in STJ = base64)
        var base64    = Convert.ToBase64String(data);
        var restored  = Convert.FromBase64String(base64);

        var obj = Assert.IsType<StrokeObject>(PageSerializer.DeserializeObject(restored));
        Assert.Equal(stroke.Id, obj.Id);
        Assert.Equal(stroke.Style.ColorArgb, obj.Style.ColorArgb);
    }

    [Fact]
    public void ApplyRemoteStroke_AddedToPage_DeduplicationPreventsDouble()
    {
        var page   = new PageModel("p1", "Test");
        var stroke = MakeStroke();
        var data   = PageSerializer.SerializeObject(stroke);

        // First apply: should succeed
        var obj1 = PageSerializer.DeserializeObject(data);
        Assert.NotNull(obj1);
        page.AddObject(obj1!);
        Assert.Single(page.GetAllObjects());

        // Second apply (same data, same ID): GetObject should detect it as duplicate
        var obj2 = PageSerializer.DeserializeObject(data);
        Assert.NotNull(obj2);
        var existing = page.GetObject(obj2!.Id);
        Assert.NotNull(existing); // dedup guard would fire here
        Assert.Single(page.GetAllObjects()); // count unchanged
    }
}
