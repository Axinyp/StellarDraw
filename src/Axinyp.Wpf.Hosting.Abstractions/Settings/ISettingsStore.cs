namespace Axinyp.Wpf.Hosting.Abstractions.Settings;

/// <summary>本地用户偏好存储。不得用于 API Key、Token、密码或其他凭据。</summary>
public interface ISettingsStore
{
    /// <summary>
    /// 读取指定键的设置值；若不存在或读取失败则返回默认值。
    /// </summary>
    /// <typeparam name="T">设置值类型。</typeparam>
    /// <param name="key">设置键。</param>
    /// <param name="defaultValue">未命中时返回的默认值。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask<T> GetAsync<T>(string key, T defaultValue, CancellationToken cancellationToken = default);

    /// <summary>
    /// 写入指定键的设置值。
    /// </summary>
    /// <typeparam name="T">设置值类型。</typeparam>
    /// <param name="key">设置键。</param>
    /// <param name="value">要保存的值。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask SetAsync<T>(string key, T value, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除指定键的设置值。
    /// </summary>
    /// <param name="key">设置键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);
}
