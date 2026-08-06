using Microsoft.Extensions.Logging;

namespace Axinyp.Wpf.Hosting.Abstractions.Wpf;

/// <summary>WPF 宿主对外稳定的结构化日志事件编号。</summary>
public static class WpfHostingEventIds
{
    /// <summary>导航成功完成。</summary>
    public static readonly EventId NavigationSucceeded = new(1100, nameof(NavigationSucceeded));

    /// <summary>导航被取消。</summary>
    public static readonly EventId NavigationCancelled = new(1101, nameof(NavigationCancelled));

    /// <summary>导航发生框架技术失败。</summary>
    public static readonly EventId NavigationFailed = new(1102, nameof(NavigationFailed));

    /// <summary>后退请求没有可用的导航记录。</summary>
    public static readonly EventId NavigationBackUnavailable = new(1103, nameof(NavigationBackUnavailable));

    /// <summary>导航提交后回收旧区域内容时失败。</summary>
    public static readonly EventId NavigationCleanupFailed = new(1104, nameof(NavigationCleanupFailed));

    /// <summary>WPF UI Contribution 目录构建完成。</summary>
    public static readonly EventId UiContributionCatalogBuilt = new(1140, nameof(UiContributionCatalogBuilt));

    /// <summary>Provider 声明的来源模块不存在。</summary>
    public static readonly EventId UiContributionProviderMissingModule = new(1141, nameof(UiContributionProviderMissingModule));

    /// <summary>Provider 无法从依赖注入容器激活。</summary>
    public static readonly EventId UiContributionProviderActivationFailed = new(1142, nameof(UiContributionProviderActivationFailed));

    /// <summary>Provider 执行贡献声明时失败。</summary>
    public static readonly EventId UiContributionProviderExecutionFailed = new(1143, nameof(UiContributionProviderExecutionFailed));

    /// <summary>Provider 返回的贡献元数据无效。</summary>
    public static readonly EventId InvalidUiContribution = new(1144, nameof(InvalidUiContribution));

    /// <summary>不同 Provider 返回了重复的贡献 ID。</summary>
    public static readonly EventId DuplicateUiContributionId = new(1145, nameof(DuplicateUiContributionId));

    /// <summary>WPF 应用宿主启动失败（已回滚并通知用户）。</summary>
    public static readonly EventId ApplicationStartupFailed = new(1160, nameof(ApplicationStartupFailed));

    /// <summary>启动失败后的宿主停止清理自身失败。</summary>
    public static readonly EventId ApplicationStartupCleanupFailed = new(1161, nameof(ApplicationStartupCleanupFailed));

    /// <summary>Shell 显示后的后续初始化失败（应用继续运行）。</summary>
    public static readonly EventId ApplicationPostStartupFailed = new(1162, nameof(ApplicationPostStartupFailed));

    /// <summary>协调停机开始。</summary>
    public static readonly EventId ApplicationShutdownStarting = new(1163, nameof(ApplicationShutdownStarting));

    /// <summary>协调停机失败；下一次关闭请求将强制退出。</summary>
    public static readonly EventId ApplicationShutdownFailed = new(1164, nameof(ApplicationShutdownFailed));

    /// <summary>协调停机完成。</summary>
    public static readonly EventId ApplicationShutdownCompleted = new(1165, nameof(ApplicationShutdownCompleted));
}
