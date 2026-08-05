using System.Windows;
using System.Windows.Controls;
using WhiteBoard.Contracts.Wpf;
using WhiteBoard.UI.Controls;

namespace WhiteBoard.Plugin;

/// <summary>
/// 白板插件视图句柄：持有已构建的根 <see cref="FrameworkElement"/>，
/// 供宿主嵌入到自身界面（<c>PluginHost.Child = handle.View</c>）。
/// <see cref="DetachAsync"/> 不仅要清根节点 DataContext，
/// 还要把插件控件树中显式持有 ViewModel/Board 的控件逐层断开，
/// 否则即便宿主已经把根视图移出视觉树，WPF 仍可能通过事件、命令或子控件引用链继续持有插件对象。
/// </summary>
public sealed class WhiteboardPluginViewHandle : IPluginViewHandle
{
    private FrameworkElement? _view;

    public string PluginId { get; }
    public FrameworkElement View => _view ?? throw new ObjectDisposedException(nameof(WhiteboardPluginViewHandle));

    public WhiteboardPluginViewHandle(string pluginId, FrameworkElement view)
    {
        PluginId = pluginId;
        _view = view;
    }

    public Task DetachAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        DetachView();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DetachView();
        return default;
    }

    private void DetachView()
    {
        if (_view is null)
            return;

        DetachTree(_view);
        _view = null;
    }

    private static void DetachTree(FrameworkElement root)
    {
        switch (root)
        {
            case InkSurface inkSurface:
                inkSurface.ShowLatency = false;
                inkSurface.ViewModel = null;
                break;
            case InkToolbar toolbar:
                toolbar.ViewModel = null;
                toolbar.Board = null;
                toolbar.Dispose();
                break;
            case PageStrip pageStrip:
                pageStrip.ViewModel = null;
                pageStrip.Dispose();
                break;
        }

        root.DataContext = null;

        if (root is Panel panel)
        {
            foreach (var child in panel.Children.OfType<FrameworkElement>().ToList())
                DetachTree(child);

            panel.Children.Clear();
            return;
        }

        if (root is ContentControl contentControl && contentControl.Content is FrameworkElement content)
        {
            DetachTree(content);
            contentControl.Content = null;
            return;
        }

        if (root is Decorator decorator && decorator.Child is FrameworkElement childElement)
        {
            DetachTree(childElement);
            decorator.Child = null;
        }
    }
}
