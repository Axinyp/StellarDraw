using System.Windows;
using WhiteBoard.Contracts.Wpf;

namespace WhiteBoard.Plugin;

/// <summary>
/// 白板插件视图句柄：持有已构建的根 <see cref="FrameworkElement"/>，
/// 供宿主嵌入到自身界面（<c>PluginHost.Child = handle.View</c>）。
/// <see cref="DetachAsync"/> 解除 DataContext 绑定，防止 UI 树卸载时继续引用 ViewModel。
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
        if (_view is not null)
        {
            _view.DataContext = null;
            _view = null;
        }
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _view = null;
        return default;
    }
}