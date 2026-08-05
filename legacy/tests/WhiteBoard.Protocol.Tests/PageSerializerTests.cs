using System.Buffers.Binary;
using System.IO.Hashing;
using WhiteBoard.Contracts.Services;
using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;
using WhiteBoard.Core.Services;

namespace WhiteBoard.Protocol.Tests;

public class PageSerializerTests
{
    private static float[] DefaultTransform() => new float[6] { 1, 0, 0, 1, 0, 0 };

    private static StrokeObject MakeStroke(float x = 0, float y = 0)
    {
        var pts = new[]
        {
            new PressurePoint(x,      y,      0.5f, 0),
            new PressurePoint(x + 10, y + 5,  0.8f, 10),
            new PressurePoint(x + 20, y + 10, 0.3f, 20),
        };
        var style = new StrokeStyle(PenType.Pen, InkType.Normal, 0xFFFF0000u, 3f, false, true);
        return new StrokeObject(Guid.NewGuid(), pts, style, x, y, 20, 10, DefaultTransform());
    }

    private static ImageObject MakeImage() =>
        new(Guid.NewGuid(), "abcdef1234567890abcdef1234567890",
            1920f, 1080f, 10, 20, 200, 100, DefaultTransform());

    private static TextObject MakeText() =>
        new(Guid.NewGuid(), "Hello, 白板!",
            new TextStyle("Arial", 16, true, false, 0xFF000000u),
            50, 50, 150, 40, DefaultTransform());

    private static DocumentObject MakeDocument() =>
        new(Guid.NewGuid(), "assets/doc/report.pdf", 5, 0, 0, 300, 400, DefaultTransform());

    // ── Round-trip tests ─────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_EmptyPage()
    {
        var page = new PageModel("p1", "Empty");
        var data = PageSerializer.Serialize(page);
        var page2 = PageSerializer.Deserialize(data, "p1");
        Assert.Empty(page2.GetAllObjects());
    }

    [Fact]
    public void RoundTrip_StrokeObject_PreservesAll()
    {
        var page   = new PageModel("p1", "T");
        var stroke = MakeStroke(5f, 10f);
        page.AddObject(stroke);

        var data  = PageSerializer.Serialize(page);
        var page2 = PageSerializer.Deserialize(data, "p1");

        Assert.Single(page2.GetAllObjects());
        var s2 = Assert.IsType<StrokeObject>(page2.GetAllObjects()[0]);

        Assert.Equal(stroke.Id, s2.Id);
        Assert.Equal(stroke.Style.ColorArgb,  s2.Style.ColorArgb);
        Assert.Equal(stroke.Style.Thickness,  s2.Style.Thickness);
        Assert.Equal(stroke.Style.PenType,    s2.Style.PenType);
        Assert.Equal(stroke.Style.IsAntiAlias, s2.Style.IsAntiAlias);
        Assert.Equal(stroke.Points.Count,     s2.Points.Count);
        Assert.Equal(stroke.Points[1].Pressure, s2.Points[1].Pressure, 5);
        Assert.Equal(stroke.BoundsX,  s2.BoundsX,  precision: 4);
        Assert.Equal(stroke.BoundsY,  s2.BoundsY,  precision: 4);
    }

    [Fact]
    public void RoundTrip_ImageObject_PreservesAssetId()
    {
        var page = new PageModel("p1", "T");
        var img  = MakeImage();
        page.AddObject(img);

        var page2 = PageSerializer.Deserialize(PageSerializer.Serialize(page), "p1");

        var img2 = Assert.IsType<ImageObject>(page2.GetAllObjects()[0]);
        Assert.Equal(img.AssetId[..32], img2.AssetId);
        Assert.Equal(img.OriginalWidth,  img2.OriginalWidth,  precision: 4);
        Assert.Equal(img.OriginalHeight, img2.OriginalHeight, precision: 4);
    }

    [Fact]
    public void RoundTrip_TextObject_PreservesContentAndStyle()
    {
        var page = new PageModel("p1", "T");
        var txt  = MakeText();
        page.AddObject(txt);

        var page2 = PageSerializer.Deserialize(PageSerializer.Serialize(page), "p1");

        var txt2 = Assert.IsType<TextObject>(page2.GetAllObjects()[0]);
        Assert.Equal(txt.Content,         txt2.Content);
        Assert.Equal(txt.Style.FontFamily, txt2.Style.FontFamily);
        Assert.Equal(txt.Style.FontSize,   txt2.Style.FontSize);
        Assert.Equal(txt.Style.Bold,       txt2.Style.Bold);
        Assert.Equal(txt.Style.ColorArgb,  txt2.Style.ColorArgb);
    }

    [Fact]
    public void RoundTrip_DocumentObject_PreservesFileRefAndPageCount()
    {
        var page = new PageModel("p1", "T");
        var doc  = MakeDocument();
        page.AddObject(doc);

        var page2 = PageSerializer.Deserialize(PageSerializer.Serialize(page), "p1");

        var doc2 = Assert.IsType<DocumentObject>(page2.GetAllObjects()[0]);
        Assert.Equal(doc.FileRef,   doc2.FileRef);
        Assert.Equal(doc.PageCount, doc2.PageCount);
    }

    [Fact]
    public void RoundTrip_MultipleObjects_PreservesCount()
    {
        var page = new PageModel("p1", "T");
        page.AddObject(MakeStroke());
        page.AddObject(MakeImage());
        page.AddObject(MakeText());
        page.AddObject(MakeDocument());

        var page2 = PageSerializer.Deserialize(PageSerializer.Serialize(page), "p1");
        Assert.Equal(4, page2.GetAllObjects().Count);
    }

    [Fact]
    public void RoundTrip_ZIndex_IsLocked_Preserved()
    {
        var page   = new PageModel("p1", "T");
        var stroke = MakeStroke();
        stroke.ZIndex   = 42;
        stroke.IsLocked = true;
        page.AddObject(stroke);

        var page2 = PageSerializer.Deserialize(PageSerializer.Serialize(page), "p1");
        var s2    = page2.GetAllObjects()[0];

        Assert.Equal(42,  s2.ZIndex);
        Assert.True(s2.IsLocked);
    }

    [Fact]
    public void Serialize_HeaderCrcCoversHeaderPrefix()
    {
        var page = new PageModel("p1", "T");
        page.AddObject(MakeStroke());

        var data = PageSerializer.Serialize(page);

        var storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(14));
        var actualCrc = Crc32.HashToUInt32(data.AsSpan(0, 14));
        Assert.Equal(actualCrc, storedCrc);
        Assert.Single(PageSerializer.Deserialize(data, "p1").GetAllObjects());
    }

    [Theory]
    [InlineData(14)]
    [InlineData(6)]
    public void Deserialize_CorruptedHeader_ReturnsEmptyPage(int offset)
    {
        var page = new PageModel("p1", "T");
        page.AddObject(MakeStroke());
        var data = PageSerializer.Serialize(page);
        data[offset] ^= 0xFF;

        var result = PageSerializer.Deserialize(data, "p1");

        Assert.Empty(result.GetAllObjects());
    }

    [Fact]
    public void RoundTrip_LegacyImageAssetId_UsesPaddedWireRepresentation()
    {
        const string legacyAssetId = "abcdef1234567890";
        var page = new PageModel("p1", "T");
        page.AddObject(new ImageObject(
            Guid.NewGuid(),
            legacyAssetId,
            1920f,
            1080f,
            10f,
            20f,
            200f,
            100f,
            DefaultTransform()));

        var result = PageSerializer.Deserialize(PageSerializer.Serialize(page), "p1");
        var image = Assert.IsType<ImageObject>(Assert.Single(result.GetAllObjects()));

        Assert.Equal(legacyAssetId.PadRight(32, '0'), image.AssetId);
    }

    // ── Robustness tests ─────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_EmptyBytes_ReturnsEmptyPage()
    {
        var page = PageSerializer.Deserialize([], "p1");
        Assert.Empty(page.GetAllObjects());
    }

    [Fact]
    public void Deserialize_InvalidMagic_ReturnsEmptyPage()
    {
        var page = PageSerializer.Deserialize(new byte[] { 0x00, 0x01, 0x02, 0x03 }, "p1");
        Assert.Empty(page.GetAllObjects());
    }

    [Fact]
    public void Deserialize_CorruptedChunk_SkipsChunk_LoadsRest()
    {
        var page = new PageModel("p1", "T");
        page.AddObject(MakeStroke(0, 0));
        page.AddObject(MakeStroke(50, 50));

        var data = PageSerializer.Serialize(page);

        // Corrupt the middle of the file (past header, inside first chunk body)
        // Header = 22 bytes; first chunk starts at offset 22
        // Corrupt bytes 30..40 (inside first chunk's body, which alters CRC mismatch)
        for (int i = 30; i < 42 && i < data.Length; i++)
            data[i] ^= 0xFF;

        var page2 = PageSerializer.Deserialize(data, "p1");

        // One chunk should be skipped (corrupted), other should survive
        Assert.Single(page2.GetAllObjects());
    }
}
