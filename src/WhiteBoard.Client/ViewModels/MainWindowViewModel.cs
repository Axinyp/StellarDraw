using CommunityToolkit.Mvvm.ComponentModel;

namespace WhiteBoard.Client.ViewModels;

/// <summary>
/// 主窗口视图模型。基于 CommunityToolkit.Mvvm，作为 DI + MVVM 骨架的最小示例；
/// 后续白板/AI 等功能以此为宿主逐步接入。
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "StellarDraw · Client — DI 骨架已就绪";
}
