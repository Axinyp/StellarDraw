using CommunityToolkit.Mvvm.ComponentModel;

namespace StellarDraw.Axinyp.Spike;

/// <summary>
/// Spike Shell 的最小状态模型。
/// 白板领域状态始终保留在 legacy plugin 内，不能穿过该消费者边界。
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _status = "Axinyp Shell 已启动；等待加载 legacy 白板插件。";
}
