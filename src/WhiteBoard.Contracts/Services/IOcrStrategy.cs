namespace WhiteBoard.Contracts.Services;

public interface IOcrStrategy
{
    string Name { get; }
    bool RequiresInternet { get; }
    string[] SupportedLanguages { get; }

    Task<OcrResult> RecognizeAsync(
        IReadOnlyList<StrokePoints> strokes,
        string? languageHint,
        CancellationToken ct);
}

public record OcrResult(bool Success, string Text, float Confidence, IReadOnlyList<OcrWord> Words);
public record OcrWord(string Text, float Confidence, OcrBounds Bounds);
public record OcrBounds(float X, float Y, float Width, float Height);

public record StrokePoints(IReadOnlyList<float[]> Points);