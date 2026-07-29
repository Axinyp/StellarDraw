namespace Axinyp.Wpf.Hosting.Abstractions.Wpf.Plugins;

/// <summary>Host Build 后冻结的 WPF 插件 UI 贡献目录。</summary>
public interface IWpfUiContributionCatalog
{
    /// <summary>
    /// 聚合后的导航页贡献。
    /// </summary>
    IReadOnlyList<WpfNavigationPageContribution> NavigationPages { get; }

    /// <summary>
    /// 聚合后的命令贡献。
    /// </summary>
    IReadOnlyList<WpfCommandContribution> Commands { get; }

    /// <summary>
    /// 聚合后的设置页贡献。
    /// </summary>
    IReadOnlyList<WpfSettingsPageContribution> SettingsPages { get; }

    /// <summary>
    /// 贡献收集过程中记录的失败信息。
    /// </summary>
    IReadOnlyList<WpfUiContributionFailure> Failures { get; }
}

/// <summary>
/// 单条 WPF UI 贡献失败信息。
/// </summary>
/// <param name="SourceModuleId">来源模块 ID。</param>
/// <param name="ProviderType">贡献提供程序类型。</param>
/// <param name="Kind">失败类型。</param>
/// <param name="Message">失败说明。</param>
public sealed record WpfUiContributionFailure(string SourceModuleId, Type ProviderType,
    WpfUiContributionFailureKind Kind, string Message);

/// <summary>
/// WPF UI 贡献失败分类。
/// </summary>
public enum WpfUiContributionFailureKind
{
    /// <summary>声明的来源模块不存在。</summary>
    MissingModule,

    /// <summary>提供程序激活失败。</summary>
    ProviderActivationFailed,

    /// <summary>提供程序执行失败。</summary>
    ProviderExecutionFailed,

    /// <summary>贡献内容本身无效。</summary>
    InvalidContribution,

    /// <summary>检测到重复贡献 ID。</summary>
    DuplicateContributionId
}
