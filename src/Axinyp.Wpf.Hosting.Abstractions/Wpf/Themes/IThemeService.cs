namespace Axinyp.Wpf.Hosting.Abstractions.Wpf.Themes;

/// <summary>
/// 主题描述。
/// </summary>
/// <param name="Id">主题唯一标识。</param>
/// <param name="DisplayName">主题显示名。</param>
/// <param name="ResourceUri">主题资源字典地址。</param>
/// <param name="IsDefault">是否为默认主题。</param>
public sealed record ThemeDescriptor(string Id, string DisplayName, Uri ResourceUri, bool IsDefault = false);

/// <summary>
/// 主题切换服务。
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// 当前已应用主题。
    /// </summary>
    ThemeDescriptor CurrentTheme { get; }

    /// <summary>
    /// 所有可用主题。
    /// </summary>
    IReadOnlyList<ThemeDescriptor> Themes { get; }

    /// <summary>
    /// 主题变化时触发。
    /// </summary>
    event EventHandler? ThemeChanged;

    /// <summary>
    /// 按主题 ID 应用主题。
    /// </summary>
    /// <param name="themeId">目标主题 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask ApplyAsync(string themeId, CancellationToken cancellationToken = default);
}
