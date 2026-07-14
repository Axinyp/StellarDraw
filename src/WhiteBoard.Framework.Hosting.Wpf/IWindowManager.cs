using System.Windows;

namespace WhiteBoard.Framework.Hosting.Wpf;

/// <summary>
/// 在 WPF UI Dispatcher 上创建和显示显式注册的窗口。
/// 每个窗口拥有独立 DI scope，在关闭或显示失败时自动释放。
/// </summary>
public interface IWindowManager
{
    /// <summary>从窗口 scope 解析 ViewModel，显示其注册窗口。</summary>
    Task<Window> ShowAsync<TViewModel>(Window? owner = null) where TViewModel : class;

    /// <summary>显示调用方已创建的 ViewModel 所对应窗口。</summary>
    Task<Window> ShowAsync(object viewModel, Window? owner = null);

    /// <summary>从窗口 scope 解析 ViewModel，以原生 WPF 模态方式显示其注册窗口。</summary>
    Task<bool?> ShowDialogAsync<TViewModel>(Window owner) where TViewModel : class;

    /// <summary>以原生 WPF 模态方式显示调用方已创建的 ViewModel 所对应窗口。</summary>
    Task<bool?> ShowDialogAsync(object viewModel, Window owner);
}
