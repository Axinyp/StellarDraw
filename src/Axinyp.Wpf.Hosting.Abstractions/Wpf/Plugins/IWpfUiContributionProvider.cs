namespace Axinyp.Wpf.Hosting.Abstractions.Wpf.Plugins;

/// <summary>
/// 插件 UI 贡献 Provider。只声明元数据，不得直接操作宿主 Window、Menu、ToolBar 或 Region 控件。
/// </summary>
public interface IWpfUiContributionProvider
{
    /// <summary>
    /// 返回当前提供程序声明的全部 WPF UI 贡献。
    /// </summary>
    WpfUiContributions GetContributions();
}

/// <summary>
/// 一组 WPF UI 贡献声明。
/// </summary>
public sealed record WpfUiContributions
{
    /// <summary>
    /// 空贡献集合。
    /// </summary>
    public static WpfUiContributions Empty { get; } = new();

    /// <summary>
    /// 导航页贡献列表。
    /// </summary>
    public IReadOnlyList<WpfNavigationPageContribution> NavigationPages { get; init; } = [];

    /// <summary>
    /// 命令贡献列表。
    /// </summary>
    public IReadOnlyList<WpfCommandContribution> Commands { get; init; } = [];

    /// <summary>
    /// 设置页贡献列表。
    /// </summary>
    public IReadOnlyList<WpfSettingsPageContribution> SettingsPages { get; init; } = [];
}

/// <summary>
/// 导航页贡献声明。
/// </summary>
/// <param name="Id">贡献唯一 ID。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="RegionName">挂载区域名称。</param>
/// <param name="ViewModelType">目标 ViewModel 类型。</param>
/// <param name="Order">排序权重。</param>
/// <param name="IconKey">可选图标键。</param>
public sealed record WpfNavigationPageContribution(string Id, string DisplayName, string RegionName,
    Type ViewModelType, int Order = 0, string? IconKey = null);

/// <summary>
/// 命令贡献声明。
/// </summary>
/// <param name="Id">贡献唯一 ID。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="CommandType">命令类型。</param>
/// <param name="Placement">命令放置位置。</param>
/// <param name="MenuPath">可选菜单路径。</param>
/// <param name="Toolbar">可选工具栏分组。</param>
/// <param name="Order">排序权重。</param>
/// <param name="IconKey">可选图标键。</param>
public sealed record WpfCommandContribution(string Id, string DisplayName, Type CommandType,
    WpfCommandPlacement Placement, string? MenuPath = null, string? Toolbar = null,
    int Order = 0, string? IconKey = null);

/// <summary>
/// 命令贡献可放置的位置。
/// </summary>
[Flags]
public enum WpfCommandPlacement
{
    /// <summary>不放置到任何宿主位置。</summary>
    None = 0,

    /// <summary>放置到菜单。</summary>
    Menu = 1,

    /// <summary>放置到工具栏。</summary>
    Toolbar = 2,

    /// <summary>同时放置到菜单和工具栏。</summary>
    MenuAndToolbar = Menu | Toolbar
}

/// <summary>
/// 设置页贡献声明。
/// </summary>
/// <param name="Id">贡献唯一 ID。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="ViewModelType">目标 ViewModel 类型。</param>
/// <param name="Category">设置分类。</param>
/// <param name="Order">排序权重。</param>
/// <param name="IconKey">可选图标键。</param>
public sealed record WpfSettingsPageContribution(string Id, string DisplayName, Type ViewModelType,
    string Category = "General", int Order = 0, string? IconKey = null);
