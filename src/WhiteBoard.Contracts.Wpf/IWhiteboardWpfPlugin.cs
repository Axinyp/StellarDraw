using System.Windows;
using WhiteBoard.Contracts.Plugin;

namespace WhiteBoard.Contracts.Wpf;

public interface IWhiteboardWpfPlugin : IPlugin
{
    /// <summary>
    /// 必须在 InitializeAsync 之后、StartAsync 之前调用；UI 线程执行。
    /// </summary>
    IPluginViewHandle CreateView();
}

public interface IPluginViewHandle : IAsyncDisposable
{
    string PluginId { get; }
    FrameworkElement View { get; }

    /// <summary>
    /// 必须在 DisposeAsync 之前调用；UI 线程执行。
    /// 清除 DataContext / CommandBinding / ResourceReference，防止 ALC 卸载时 WPF 引用残留。
    /// </summary>
    Task DetachAsync(CancellationToken ct);
}