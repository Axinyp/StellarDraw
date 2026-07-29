using CommunityToolkit.Mvvm.ComponentModel;

namespace Axinyp.Wpf.Hosting.Samples.BasicHost.ViewModels;

/// <summary>
/// 主窗口视图模型。
/// 作为 Generic Host + WPF + MVVM 的最小样例，展示基础宿主已完成启动。
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Axinyp.Wpf.Hosting · BasicHost 已启动";
}
