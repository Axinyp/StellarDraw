using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WhiteBoard.UI.ViewModels;

namespace WhiteBoard.UI.Controls;

/// <summary>
/// 水平工具栏：工具选择、颜色色块、笔粗调节。
/// DataContext 不使用；通过 ViewModel DependencyProperty 绑定 ToolbarViewModel。
/// </summary>
public sealed class InkToolbar : UserControl
{
    // ── DependencyProperty: ViewModel ────────────────────────────────────────

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(ToolbarViewModel),
            typeof(InkToolbar), new PropertyMetadata(null, OnViewModelChanged));

    public ToolbarViewModel? ViewModel
    {
        get => (ToolbarViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly Dictionary<ToolType, Button> _toolButtons = new();
    private readonly Border    _colorSwatch;
    private readonly Slider    _thicknessSlider;
    private readonly TextBlock _thicknessText;
    private          bool      _suppressSlider;

    // ── Definitions ───────────────────────────────────────────────────────────

    private static readonly (ToolType Tool, string Label, string Tip)[] ToolDefs =
    [
        (ToolType.Pen,         "钢笔", "钢笔"),
        (ToolType.Highlighter, "荧光", "荧光笔"),
        (ToolType.Marker,      "记号", "记号笔"),
        (ToolType.Eraser,      "橡皮", "橡皮擦"),
        (ToolType.Hand,        "平移", "平移 (空格)"),
    ];

    private static readonly uint[] PresetColors =
    [
        0xFF000000u,  // 黑
        0xFFE53935u,  // 红
        0xFF1E88E5u,  // 蓝
        0xFF43A047u,  // 绿
        0xFFFF8F00u,  // 橙
        0xFF8E24AAu,  // 紫
    ];

    // ── Constructor ───────────────────────────────────────────────────────────

    public InkToolbar()
    {
        Height = 48;
        (_colorSwatch, _thicknessSlider, _thicknessText) = BuildContent();
    }

    private (Border, Slider, TextBlock) BuildContent()
    {
        var panel = new StackPanel
        {
            Orientation       = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(8, 0, 8, 0),
        };

        // Tool buttons
        foreach (var (tool, label, tip) in ToolDefs)
        {
            var btn = new Button
            {
                Content         = new TextBlock { Text = label, FontSize = 11 },
                Width           = 42,
                Height          = 34,
                Margin          = new Thickness(2, 0, 2, 0),
                ToolTip         = tip,
                Cursor          = Cursors.Hand,
                Tag             = tool,
                BorderThickness = new Thickness(1),
            };
            btn.Click += OnToolClicked;
            _toolButtons[tool] = btn;
            panel.Children.Add(btn);
        }

        panel.Children.Add(MakeSep());

        // Color swatches
        foreach (var argb in PresetColors)
        {
            var captured = argb;
            var colorBtn = new Button
            {
                Width           = 22,
                Height          = 22,
                Margin          = new Thickness(2, 0, 2, 0),
                Background      = new SolidColorBrush(ArgbToColor(captured)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)),
                BorderThickness = new Thickness(1),
                Cursor          = Cursors.Hand,
                ToolTip         = $"#{captured & 0xFFFFFF:X6}",
            };
            colorBtn.Click += (_, _) => { if (ViewModel is { } vm) vm.PenColor = captured; };
            panel.Children.Add(colorBtn);
        }

        // Current-color swatch
        var swatch = new Border
        {
            Width             = 24,
            Height            = 24,
            Margin            = new Thickness(6, 0, 2, 0),
            BorderBrush       = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            BorderThickness   = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Background        = Brushes.Black,
            ToolTip           = "当前颜色",
        };
        panel.Children.Add(swatch);

        panel.Children.Add(MakeSep());

        // Thickness label + slider + text
        panel.Children.Add(new TextBlock
        {
            Text              = "粗细:",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize          = 12,
            Foreground        = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            Margin            = new Thickness(0, 0, 4, 0),
        });

        var slider = new Slider
        {
            Minimum           = 1,
            Maximum           = 20,
            Value             = 3,
            LargeChange       = 2,
            SmallChange       = 0.5,
            Width             = 80,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(slider);

        var thickText = new TextBlock
        {
            Text              = "3px",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize          = 11,
            Foreground        = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            Margin            = new Thickness(4, 0, 0, 0),
            Width             = 28,
        };
        panel.Children.Add(thickText);

        slider.ValueChanged += (_, e) =>
        {
            thickText.Text = $"{e.NewValue:F0}px";
            if (!_suppressSlider && ViewModel is { } vm)
                vm.Thickness = (float)e.NewValue;
        };

        Content = new Border
        {
            Background      = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child           = panel,
        };
        return (swatch, slider, thickText);
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnToolClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not Button { Tag: ToolType tool }) return;

        ICommand? cmd = tool switch
        {
            ToolType.Pen         => ViewModel.SelectPenCommand,
            ToolType.Highlighter => ViewModel.SelectHighlighterCommand,
            ToolType.Marker      => ViewModel.SelectMarkerCommand,
            ToolType.Eraser      => ViewModel.SelectEraserCommand,
            ToolType.Hand        => ViewModel.SelectHandCommand,
            _                    => null,
        };
        cmd?.Execute(null);
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var tb = (InkToolbar)d;
        if (e.OldValue is ToolbarViewModel old)
            old.PropertyChanged -= tb.OnVmPropertyChanged;
        if (e.NewValue is ToolbarViewModel vm)
        {
            vm.PropertyChanged += tb.OnVmPropertyChanged;
            tb.SyncAll(vm);
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ToolbarViewModel vm) return;
        switch (e.PropertyName)
        {
            case nameof(ToolbarViewModel.CurrentTool):
                SyncToolButtons(vm.CurrentTool);
                break;
            case nameof(ToolbarViewModel.PenColor):
                _colorSwatch.Background = new SolidColorBrush(ArgbToColor(vm.PenColor));
                break;
            case nameof(ToolbarViewModel.Thickness):
                _suppressSlider = true;
                _thicknessSlider.Value = vm.Thickness;
                _suppressSlider = false;
                _thicknessText.Text = $"{vm.Thickness:F0}px";
                break;
        }
    }

    private void SyncAll(ToolbarViewModel vm)
    {
        SyncToolButtons(vm.CurrentTool);
        _colorSwatch.Background = new SolidColorBrush(ArgbToColor(vm.PenColor));
        _suppressSlider = true;
        _thicknessSlider.Value = vm.Thickness;
        _suppressSlider = false;
        _thicknessText.Text = $"{vm.Thickness:F0}px";
    }

    private void SyncToolButtons(ToolType active)
    {
        var activeBg     = new SolidColorBrush(Color.FromRgb(0xCC, 0xE4, 0xFF));
        var activeBorder = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
        var normalBg     = new SolidColorBrush(Color.FromRgb(0xEB, 0xEB, 0xEB));
        var normalBorder = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));

        foreach (var (tool, btn) in _toolButtons)
        {
            btn.Background  = tool == active ? activeBg     : normalBg;
            btn.BorderBrush = tool == active ? activeBorder : normalBorder;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static UIElement MakeSep() => new Rectangle
    {
        Width             = 1,
        Height            = 28,
        Fill              = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
        VerticalAlignment = VerticalAlignment.Center,
        Margin            = new Thickness(8, 0, 8, 0),
    };

    private static Color ArgbToColor(uint argb)
        => Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte) argb);
}
