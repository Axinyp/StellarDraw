using System.Windows;

namespace ConsumerSmoke;

/// <summary>
/// 本地包消费验证窗口。
/// DataContext 由 IWindowManager 统一注入，
/// 用于验证消费者项目无需自己手动绑定 ViewModel。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 初始化窗口视图。
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
    }
}
