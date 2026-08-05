using CommunityToolkit.Mvvm.ComponentModel;

namespace WhiteBoard.Client.ViewModels;

/// <summary>
/// 用于验证 Axinyp Region 导航链路的最小首页 ViewModel。
/// 不连接白板领域、AI、MCP、协同或 legacy 插件。
/// </summary>
public sealed partial class ClientHomeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _message = "Axinyp Region 导航已完成。";
}
