namespace Axinyp.Wpf.Hosting.Abstractions.Wpf.Themes;

/// <summary>
/// 主题服务配置选项。
/// </summary>
public sealed class ThemeOptions
{
    /// <summary>
    /// 写入设置存储的主题键名。
    /// </summary>
    public string SettingsKey { get; set; } = "ui.theme";

    /// <summary>
    /// 可用主题列表。
    /// </summary>
    public IList<ThemeDescriptor> Themes { get; } = [];
}
