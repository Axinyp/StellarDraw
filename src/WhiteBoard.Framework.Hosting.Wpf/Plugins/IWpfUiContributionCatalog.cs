namespace WhiteBoard.Framework.Hosting.Wpf.Plugins;

/// <summary>Host Build 后冻结的 WPF 插件 UI 贡献目录。</summary>
public interface IWpfUiContributionCatalog
{
    IReadOnlyList<WpfNavigationPageContribution> NavigationPages { get; }
    IReadOnlyList<WpfCommandContribution> Commands { get; }
    IReadOnlyList<WpfSettingsPageContribution> SettingsPages { get; }
    IReadOnlyList<WpfUiContributionFailure> Failures { get; }
}

public sealed record WpfUiContributionFailure(string SourceModuleId, Type ProviderType,
    WpfUiContributionFailureKind Kind, string Message);

public enum WpfUiContributionFailureKind
{
    MissingModule,
    ProviderActivationFailed,
    ProviderExecutionFailed,
    InvalidContribution,
    DuplicateContributionId
}
