namespace Axinyp.Wpf.Hosting.Abstractions.Localization;

/// <summary>
/// 本地化服务配置。
/// 指定文化持久化键、默认文化、支持文化以及参与查找的资源管理器集合。
/// </summary>
public sealed class LocalizationOptions
{
    /// <summary>
    /// 写入 <see cref="Settings.ISettingsStore"/> 的文化设置键名。
    /// </summary>
    public string SettingsKey { get; set; } = "ui.culture";

    /// <summary>
    /// 未找到已持久化文化或配置非法时使用的默认文化。
    /// </summary>
    public CultureInfo DefaultCulture { get; set; } = CultureInfo.GetCultureInfo("zh-CN");

    /// <summary>
    /// 允许切换到的文化列表。
    /// </summary>
    public IList<CultureInfo> SupportedCultures { get; } = [];

    /// <summary>
    /// 参与字符串查找的资源管理器集合。
    /// 按注册顺序查找，首个命中的值即为结果。
    /// </summary>
    public IList<ResourceManager> ResourceManagers { get; } = [];
}
