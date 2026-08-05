using System.Windows;

namespace Axinyp.Wpf.Hosting.Abstractions.Wpf;

/// <summary>已注册的内容区域。区域只承载当前激活的 FrameworkElement，不缓存历史视觉树。</summary>
public interface IRegion
{
    /// <summary>
    /// 区域名称。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 当前激活的视图实例。
    /// </summary>
    FrameworkElement? ActiveView { get; }

    /// <summary>
    /// 当前激活视图对应的 ViewModel。
    /// </summary>
    object? ActiveViewModel { get; }
}
