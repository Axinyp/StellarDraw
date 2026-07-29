namespace Axinyp.Wpf.Hosting.Abstractions.Settings;

/// <summary>
/// 本地 JSON 偏好存储选项。
/// 默认应用名改为中性名称，避免将业务产品名泄漏到通用框架消费者目录结构中。
/// </summary>
public sealed class SettingsStoreOptions
{
    /// <summary>
    /// 应用名；默认用于拼接 LocalApplicationData 下的设置目录。
    /// </summary>
    public string ApplicationName { get; set; } = "Axinyp.Wpf.Hosting.App";

    /// <summary>
    /// 显式指定设置目录；为 null 时回退到基于 <see cref="ApplicationName"/> 的默认目录。
    /// </summary>
    public string? DirectoryPath { get; set; }

    /// <summary>
    /// 设置文件名。
    /// </summary>
    public string FileName { get; set; } = "settings.json";

    /// <summary>
    /// 解析当前配置对应的设置文件完整路径。
    /// </summary>
    public string GetFilePath() => Path.Combine(
        DirectoryPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ApplicationName),
        FileName);
}
