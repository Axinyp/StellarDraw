using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WhiteBoard.Core.Domain;
using WhiteBoard.UI.ViewModels;

namespace WhiteBoard.UI.Controls;

/// <summary>
/// 底部页面标签条：展示所有页面、支持切换、添加新页。
/// 通过 ViewModel DependencyProperty 绑定 WhiteboardViewModel（包含 Pages + AddPageCommand）。
/// </summary>
public sealed class PageStrip : UserControl
{
    // ── DependencyProperty: ViewModel ────────────────────────────────────────

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(WhiteboardViewModel),
            typeof(PageStrip), new PropertyMetadata(null, OnViewModelChanged));

    public WhiteboardViewModel? ViewModel
    {
        get => (WhiteboardViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly StackPanel _pagePanel;

    // ── Constructor ───────────────────────────────────────────────────────────

    public PageStrip()
    {
        Height = 36;
        _pagePanel = BuildContent();
    }

    private StackPanel BuildContent()
    {
        var pagePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Content = pagePanel,
        };

        var addBtn = new Button
        {
            Content = "+",
            Width = 28,
            Height = 26,
            Margin = new Thickness(4, 0, 4, 0),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            ToolTip = "添加页面",
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        addBtn.Click += (_, _) => ViewModel?.AddPageCommand.Execute(null);

        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(addBtn, Dock.Right);
        dock.Children.Add(addBtn);
        dock.Children.Add(scroll);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = dock,
        };
        return pagePanel;
    }

    // ── ViewModel binding ─────────────────────────────────────────────────────

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var strip = (PageStrip)d;

        if (e.OldValue is WhiteboardViewModel old)
        {
            old.Pages.PropertyChanged -= strip.OnPagesPropertyChanged;
            old.Pages.Pages.CollectionChanged -= strip.OnCollectionChanged;
        }

        if (e.NewValue is WhiteboardViewModel vm)
        {
            vm.Pages.PropertyChanged += strip.OnPagesPropertyChanged;
            vm.Pages.Pages.CollectionChanged += strip.OnCollectionChanged;
            strip.Rebuild(vm);
        }
    }

    private void OnPagesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PageListViewModel.CurrentPage) && ViewModel is { } vm)
            UpdateActiveState(vm);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (ViewModel is { } vm) Rebuild(vm);
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    private void Rebuild(WhiteboardViewModel vm)
    {
        _pagePanel.Children.Clear();
        int num = 1;
        foreach (var page in vm.Pages.Pages)
        {
            var p = page;
            var btn = new Button
            {
                Height = 26,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(2, 0, 2, 0),
                FontSize = 12,
                Cursor = Cursors.Hand,
                Tag = p,
                VerticalAlignment = VerticalAlignment.Center,
                BorderThickness = new Thickness(1),
            };
            btn.Content = (string.IsNullOrWhiteSpace(p.Title) || p.Title == "Page")
                ? $"第 {num} 页"
                : p.Title;
            btn.Click += (_, _) => ViewModel?.Pages.GoToPage(p.Id);
            _pagePanel.Children.Add(btn);
            num++;
        }
        UpdateActiveState(vm);
    }

    private void UpdateActiveState(WhiteboardViewModel vm)
    {
        var current = vm.Pages.CurrentPage;
        foreach (var child in _pagePanel.Children.OfType<Button>())
        {
            bool active = child.Tag is PageModel p && ReferenceEquals(p, current);
            child.Background = new SolidColorBrush(active
                ? Color.FromRgb(0xCC, 0xE4, 0xFF)
                : Color.FromRgb(0xE8, 0xE8, 0xE8));
            child.BorderBrush = new SolidColorBrush(active
                ? Color.FromRgb(0x00, 0x78, 0xD4)
                : Color.FromRgb(0xCC, 0xCC, 0xCC));
        }
    }
}