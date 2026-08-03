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
/// <param name="Message">面向 consumer 的稳定失败说明，不包含原始异常消息。</param>
public sealed record WpfUiContributionFailure(string SourceModuleId, Type ProviderType,
    WpfUiContributionFailureKind Kind, string Message)
{
    /// <summary>供 consumer 和日志关联的稳定诊断代码。</summary>
    public string DiagnosticCode => $"wpf.ui-contribution.{Kind}";

    /// <summary>与失败类型对应的结构化日志事件编号。</summary>
    public int EventId => Kind switch
    {
        WpfUiContributionFailureKind.MissingModule => WpfHostingEventIds.UiContributionProviderMissingModule.Id,
        WpfUiContributionFailureKind.ProviderActivationFailed => WpfHostingEventIds.UiContributionProviderActivationFailed.Id,
        WpfUiContributionFailureKind.ProviderExecutionFailed => WpfHostingEventIds.UiContributionProviderExecutionFailed.Id,
        WpfUiContributionFailureKind.InvalidContribution => WpfHostingEventIds.InvalidUiContribution.Id,
        WpfUiContributionFailureKind.DuplicateContributionId => WpfHostingEventIds.DuplicateUiContributionId.Id,
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "未知的 UI 贡献失败类型。")
    };

    /// <summary>仅记录异常类型名，不持有异常实例。</summary>
    public string? ExceptionType { get; init; }

    /// <summary>无效或重复贡献的类别；不适用时为 <see langword="null"/>。</summary>
    public string? ContributionKind { get; init; }

    /// <summary>无效或重复贡献的 ID；不适用时为 <see langword="null"/>。</summary>
    public string? ContributionId { get; init; }
}

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
