namespace WhiteBoard.Protocol.Dtos;

public class WhiteboardDocument
{
    public int             FormatVersion    { get; set; } = 1;
    public string          DocumentId       { get; set; } = Guid.NewGuid().ToString();
    public string          GeneratorVersion { get; set; } = "1.0.0";
    public DateTimeOffset  CreatedUtc       { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset  ModifiedUtc      { get; set; } = DateTimeOffset.UtcNow;
    public int             PageCount        { get; set; }
    public List<string>    FeatureFlags     { get; set; } = [];
    public List<PageEntry> Pages            { get; set; } = [];
    public List<AssetEntry> Assets          { get; set; } = [];

    public record PageEntry(string Id, string Title, string Background);
    public record AssetEntry(string Id, string MimeType, int RefCount, long SizeBytes);
}
