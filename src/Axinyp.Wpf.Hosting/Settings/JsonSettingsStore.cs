using System.Text.Json;

namespace Axinyp.Wpf.Hosting.Settings;

/// <summary>线程安全的单文件 JSON 偏好存储；以临时文件替换避免半写入损坏。</summary>
internal sealed class JsonSettingsStore(SettingsStoreOptions options) : ISettingsStore
{
    private static readonly string[] ForbiddenSegments = ["secret", "password", "passwd", "pwd", "token", "jwt", "cookie", "apikey", "api-key", "credential", "privatekey", "private-key"];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, JsonElement> _values = new(StringComparer.Ordinal);
    private bool _loaded;

    public async ValueTask<T> GetAsync<T>(string key, T defaultValue, CancellationToken cancellationToken = default)
    {
        ValidateKey(key); await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _values.TryGetValue(key, out var value)
                ? value.Deserialize<T>() ?? defaultValue
                : defaultValue;
        }
        catch (JsonException) { return defaultValue; }
        finally { _gate.Release(); }
    }

    public async ValueTask SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        ValidateKey(key); await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _values[key] = JsonSerializer.SerializeToElement(value); await SaveLockedAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key); await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var removed = _values.Remove(key);
            if (removed) await SaveLockedAsync(cancellationToken).ConfigureAwait(false);
            return removed;
        }
        finally { _gate.Release(); }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded) return;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loaded) return;
            var path = options.GetFilePath();
            if (File.Exists(path))
            {
                try
                {
                    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
                    if (document.RootElement.TryGetProperty("values", out var values))
                        foreach (var item in values.EnumerateObject()) _values[item.Name] = item.Value.Clone();
                }
                catch (JsonException)
                {
                    File.Move(path, $"{path}.bad.{DateTime.UtcNow:yyyyMMddHHmmss}", overwrite: true);
                }
            }
            _loaded = true;
        }
        finally { _gate.Release(); }
    }

    private async Task SaveLockedAsync(CancellationToken cancellationToken)
    {
        var path = options.GetFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = $"{path}.tmp";
        var json = JsonSerializer.Serialize(new { schemaVersion = 1, values = _values });
        await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || key.Any(c => !(char.IsLetterOrDigit(c) || c is '.' or '_' or '-')))
            throw new ArgumentException("设置键必须为不超过 128 个字符的字母、数字、点、下划线或连字符。", nameof(key));
        if (ForbiddenSegments.Any(segment => key.Contains(segment, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("ISettingsStore 仅用于用户偏好，禁止保存凭据或敏感值。", nameof(key));
    }
}
