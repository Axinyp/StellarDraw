using System.Windows;

namespace WhiteBoard.Framework.Hosting.Wpf;

/// <summary>已注册的内容区域。区域只承载当前激活的 FrameworkElement，不缓存历史视觉树。</summary>
public interface IRegion
{
    string Name { get; }

    FrameworkElement? ActiveView { get; }

    object? ActiveViewModel { get; }
}
