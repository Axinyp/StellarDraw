using CommunityToolkit.Mvvm.ComponentModel;

namespace ConsumerSmoke;

/// <summary>
/// 本地包消费验证视图模型。
/// 内容简单即可，重点是验证 CommunityToolkit.Mvvm 与宿主包组合接入无冲突。
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "ConsumerSmoke 已通过本地包完成启动";
}
