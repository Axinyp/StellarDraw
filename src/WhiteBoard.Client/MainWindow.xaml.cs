using System.Windows;
using WhiteBoard.Client.ViewModels;

namespace WhiteBoard.Client;

/// <summary>
/// 主窗口仅处理有效画布尺寸同步；白板 Session 生命周期由 Generic Host 模块管理。
/// </summary>
public partial class MainWindow : Window
{
    private Size _lastCanvasSize;
    private bool _isSyncingCanvasSize;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await SynchronizeCanvasSizeAsync(CanvasSurface.ActualWidth, CanvasSurface.ActualHeight);
    }

    private async void OnCanvasSurfaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        await SynchronizeCanvasSizeAsync(e.NewSize.Width, e.NewSize.Height);
    }

    private async Task SynchronizeCanvasSizeAsync(double width, double height)
    {
        if (_isSyncingCanvasSize || DataContext is not MainWindowViewModel viewModel ||
            width <= 0 || height <= 0 ||
            Math.Abs(_lastCanvasSize.Width - width) < 0.5 && Math.Abs(_lastCanvasSize.Height - height) < 0.5)
            return;

        _isSyncingCanvasSize = true;
        try
        {
            await viewModel.Board.SetCanvasSizeAsync((float)width, (float)height);
            _lastCanvasSize = new Size(width, height);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Client] Failed to sync canvas size: {ex.GetType().Name}");
        }
        finally
        {
            _isSyncingCanvasSize = false;
        }
    }
}
