using System.Windows.Input;

namespace WhiteBoard.Framework.Hosting.Wpf.Plugins;

/// <summary>插件 UI 贡献 Provider。只声明元数据，不得直接操作宿主 Window、Menu、ToolBar 或 Region 控件。</summary>
public interface IWpfUiContributionProvider
{
    WpfUiContributions GetContributions();
}

public sealed record WpfUiContributions
{
    public static WpfUiContributions Empty { get; } = new();
    public IReadOnlyList<WpfNavigationPageContribution> NavigationPages { get; init; } = [];
    public IReadOnlyList<WpfCommandContribution> Commands { get; init; } = [];
    public IReadOnlyList<WpfSettingsPageContribution> SettingsPages { get; init; } = [];
}

public sealed record WpfNavigationPageContribution(string Id, string DisplayName, string RegionName,
    Type ViewModelType, int Order = 0, string? IconKey = null);

public sealed record WpfCommandContribution(string Id, string DisplayName, Type CommandType,
    WpfCommandPlacement Placement, string? MenuPath = null, string? Toolbar = null,
    int Order = 0, string? IconKey = null);

[Flags]
public enum WpfCommandPlacement { None = 0, Menu = 1, Toolbar = 2, MenuAndToolbar = Menu | Toolbar }

public sealed record WpfSettingsPageContribution(string Id, string DisplayName, Type ViewModelType,
    string Category = "General", int Order = 0, string? IconKey = null);
