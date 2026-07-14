using System.Windows;

namespace WhiteBoard.Client;

/// <summary>
/// 主窗口。DataContext 由 IWindowManager 在显示时统一绑定，
/// 避免 View 构造和窗口管理路径分别解析出不同的 ViewModel 实例。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
