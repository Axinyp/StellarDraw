namespace Axinyp.Wpf.Hosting.Abstractions.Localization;

/// <summary>
/// 本地化服务抽象。
/// 负责维护当前文化、解析资源字符串，并在文化切换时通知上层刷新绑定或文本。
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// 当前正在使用的文化。
    /// </summary>
    CultureInfo CurrentCulture { get; }

    /// <summary>
    /// 当前文化发生变化时触发。
    /// </summary>
    event EventHandler? CultureChanged;

    /// <summary>
    /// 按当前文化读取指定资源键对应的字符串。
    /// </summary>
    /// <param name="key">资源键。</param>
    string GetString(string key);

    /// <summary>
    /// 将当前文化切换为指定值，并持久化到设置存储。
    /// </summary>
    /// <param name="culture">目标文化。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask SetCultureAsync(CultureInfo culture, CancellationToken cancellationToken = default);
}
