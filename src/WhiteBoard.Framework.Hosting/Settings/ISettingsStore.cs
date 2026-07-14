namespace WhiteBoard.Framework.Hosting.Settings;

/// <summary>本地用户偏好存储。不得用于 API Key、Token、密码或其他凭据。</summary>
public interface ISettingsStore
{
    ValueTask<T> GetAsync<T>(string key, T defaultValue, CancellationToken cancellationToken = default);
    ValueTask SetAsync<T>(string key, T value, CancellationToken cancellationToken = default);
    ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);
}
