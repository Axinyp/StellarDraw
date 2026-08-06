using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axinyp.Wpf.Hosting.Settings;

/// <summary>线程安全的单文件 JSON 偏好存储；以临时文件替换避免半写入损坏。</summary>
internal sealed class JsonSettingsStore(SettingsStoreOptions options, ILogger<JsonSettingsStore>? logger = null) : ISettingsStore
{
    private static readonly EventId SettingsLoadFailed = new(1060, nameof(SettingsLoadFailed));
    private static readonly EventId SettingsQuarantineFailed = new(1061, nameof(SettingsQuarantineFailed));

    private static readonly string[] ForbiddenSegments = ["secret", "password", "passwd", "pwd", "token", "jwt", "cookie", "apikey", "api-key", "credential", "privatekey", "private-key"];
    private readonly ILogger _logger = logger ?? NullLogger<JsonSettingsStore>.Instance;
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
        try
        {
            // 先提交内存再落盘会在保存失败时留下「从未持久化的值」，
            // 并被下一次任意键的成功保存顺带写入；因此失败时回滚内存，保持内存与磁盘一致。
            var hadPrevious = _values.TryGetValue(key, out var previous);
            _values[key] = JsonSerializer.SerializeToElement(value);
            try
            {
                await SaveLockedAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (hadPrevious) _values[key] = previous;
                else _values.Remove(key);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key); await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_values.TryGetValue(key, out var previous)) return false;
            _values.Remove(key);
            try
            {
                await SaveLockedAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _values[key] = previous;
                throw;
            }
            return true;
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
                    QuarantineCorruptFile(path);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // 设置文件被锁定（杀软/同步工具）或不可读时降级为内存空存储：
                    // 用户偏好不可用绝不能沿启动任务把整个宿主启动炸掉。
                    _logger.LogWarning(SettingsLoadFailed, exception,
                        "无法读取设置文件，已降级为内存空存储；本次会话的偏好可能无法持久化。");
                }
            }
            _loaded = true;
        }
        finally { _gate.Release(); }
    }

    private void QuarantineCorruptFile(string path)
    {
        try
        {
            File.Move(path, $"{path}.bad.{DateTime.UtcNow:yyyyMMddHHmmss}", overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(SettingsQuarantineFailed, exception, "隔离损坏的设置文件失败，已忽略其内容。");
        }
    }

    private async Task SaveLockedAsync(CancellationToken cancellationToken)
    {
        var path = options.GetFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // GUID 临时名避免多进程共用固定 .tmp 冲突；WriteThrough 确保 rename 前数据真正落盘，
        // 否则断电仍可能留下空文件——这正是原子替换要防的事。
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, values = _values });
        try
        {
            await using (var stream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || key.Any(c => !(char.IsLetterOrDigit(c) || c is '.' or '_' or '-')))
            throw new ArgumentException("设置键必须为不超过 128 个字符的字母、数字、点、下划线或连字符。", nameof(key));
        if (ForbiddenSegments.Any(segment => key.Contains(segment, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("ISettingsStore 仅用于用户偏好，禁止保存凭据或敏感值。", nameof(key));
    }
}
