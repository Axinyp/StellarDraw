using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using WhiteBoard.UI.ViewModels;

namespace WhiteBoard.UI.Views;

// ── Converters ─────────────────────────────────────────────────────────────────

[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is not true;

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => value is not true;
}

// ── View ───────────────────────────────────────────────────────────────────────

public partial class AiChatView : UserControl
{
    public AiChatView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private AiChatViewModel? ViewModel => DataContext as AiChatViewModel;

    // Wire message collection changes for auto-scroll
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is AiChatViewModel old)
            old.Messages.CollectionChanged -= OnMessagesChanged;

        if (e.NewValue is AiChatViewModel vm)
            vm.Messages.CollectionChanged += OnMessagesChanged;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() => MessagesScroll.ScrollToEnd(),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    // PTT: start recording on press, stop on release
    private void PttBtn_MouseDown(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.PttStartCommand.Execute(null);
        ((UIElement)sender).CaptureMouse();
    }

    private void PttBtn_MouseUp(object sender, MouseButtonEventArgs e)
    {
        ((UIElement)sender).ReleaseMouseCapture();
        ViewModel?.PttStopCommand.Execute(null);
    }
}
