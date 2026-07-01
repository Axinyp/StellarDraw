using WhiteBoard.Contracts.Services;

namespace WhiteBoard.Core.Services;

/// <summary>
/// OCR 服务：管理多个 <see cref="IOcrStrategy"/> 实现（例如云端/本地），
/// 通过 <see cref="Activate"/> 指定当前活跃策略，无策略时返回空结果而非抛异常。
/// </summary>
public sealed class OcrService
{
    private readonly Dictionary<string, IOcrStrategy> _strategies = new(StringComparer.OrdinalIgnoreCase);
    private IOcrStrategy? _active;

    public void Register(IOcrStrategy strategy)
    {
        if (strategy is null) throw new ArgumentNullException(nameof(strategy));
        _strategies[strategy.Name] = strategy;
    }

    public void Activate(string name)
    {
        if (!_strategies.TryGetValue(name, out var s))
            throw new KeyNotFoundException($"OCR strategy '{name}' is not registered.");
        Interlocked.Exchange(ref _active, s);
    }

    public bool IsAvailable => Volatile.Read(ref _active) != null;

    public IEnumerable<string> RegisteredStrategies => _strategies.Keys;

    public Task<OcrResult> RecognizeAsync(
        IReadOnlyList<StrokePoints> strokes,
        string? languageHint = null,
        CancellationToken ct = default)
    {
        var strategy = Volatile.Read(ref _active);
        if (strategy is null)
            return Task.FromResult(new OcrResult(false, string.Empty, 0f, []));
        return strategy.RecognizeAsync(strokes, languageHint, ct);
    }
}