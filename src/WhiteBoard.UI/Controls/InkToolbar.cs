using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WhiteBoard.UI.ViewModels;

namespace WhiteBoard.UI.Controls;

/// <summary>
/// 水平工具栏：绘图工具选择 + 颜色/粗细 + 撤销/重做/清空/形状/图片操作按钮。
/// 通过 ViewModel（ToolbarViewModel）和 Board（WhiteboardViewModel）两个 DP 绑定。
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

    // ── DependencyProperty: Board ─────────────────────────────────────────────

    public static readonly DependencyProperty BoardProperty =
        DependencyProperty.Register(nameof(Board), typeof(WhiteboardViewModel),
            typeof(InkToolbar), new PropertyMetadata(null));

    public WhiteboardViewModel? Board
    {
        get => (WhiteboardViewModel?)GetValue(BoardProperty);
        set => SetValue(BoardProperty, value);
    }

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly Dictionary<ToolType, Button> _toolButtons = new();
    private readonly Border _colorSwatch;
    private readonly Slider _thicknessSlider;
    private readonly TextBlock _thicknessText;
    private bool _suppressSlider;

    // ── Definitions ───────────────────────────────────────────────────────────

    private static readonly (ToolType Tool, string Label, string Tip)[] ToolDefs =
    [
        (ToolType.Pen,         "钢笔", "钢笔"),
        (ToolType.Highlighter, "荧光", "荧光笔"),
        (ToolType.Marker,      "记号", "记号笔"),
        (ToolType.Eraser,      "橡皮", "橡皮擦"),
        (ToolType.Select,      "选择", "框选对象"),
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

    private static readonly (string Type, string Label)[] ShapeDefs =
    [
        ("rectangle", "矩形"),
        ("circle",    "圆形"),
        ("triangle",  "三角形"),
        ("arrow",     "箭头"),
        ("line",      "直线"),
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
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
        };

        // ── 绘图工具按钮 ──────────────────────────────────────────────────────
        foreach (var (tool, label, tip) in ToolDefs)
        {
            var btn = new Button
            {
                Content = new TextBlock { Text = label, FontSize = 11 },
                Width = 42,
                Height = 34,
                Margin = new Thickness(2, 0, 2, 0),
                ToolTip = tip,
                Cursor = Cursors.Hand,
                Tag = tool,
                BorderThickness = new Thickness(1),
            };
            btn.Click += OnToolClicked;
            _toolButtons[tool] = btn;
            panel.Children.Add(btn);
        }

        panel.Children.Add(MakeSep());

        // ── 颜色色块 ──────────────────────────────────────────────────────────
        foreach (var argb in PresetColors)
        {
            var captured = argb;
            var colorBtn = new Button
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(2, 0, 2, 0),
                Background = new SolidColorBrush(ArgbToColor(captured)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = $"#{captured & 0xFFFFFF:X6}",
            };
            colorBtn.Click += (_, _) => { if (ViewModel is { } vm) vm.PenColor = captured; };
            panel.Children.Add(colorBtn);
        }

        // 当前色块
        var swatch = new Border
        {
            Width = 24,
            Height = 24,
            Margin = new Thickness(6, 0, 2, 0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Black,
            ToolTip = "当前颜色",
        };
        panel.Children.Add(swatch);

        panel.Children.Add(MakeSep());

        // ── 笔粗 ─────────────────────────────────────────────────────────────
        panel.Children.Add(new TextBlock
        {
            Text = "粗细:",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            Margin = new Thickness(0, 0, 4, 0),
        });

        var slider = new Slider
        {
            Minimum = 1,
            Maximum = 20,
            Value = 3,
            LargeChange = 2,
            SmallChange = 0.5,
            Width = 80,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(slider);

        var thickText = new TextBlock
        {
            Text = "3px",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            Margin = new Thickness(4, 0, 0, 0),
            Width = 28,
        };
        panel.Children.Add(thickText);

        slider.ValueChanged += (_, e) =>
        {
            thickText.Text = $"{e.NewValue:F0}px";
            if (!_suppressSlider && ViewModel is { } vm)
                vm.Thickness = (float)e.NewValue;
        };

        panel.Children.Add(MakeSep());

        // ── 操作按钮区 ────────────────────────────────────────────────────────
        panel.Children.Add(MakeActionBtn("↩", "撤销 (Ctrl+Z)",
            () => Board?.UndoCommand.Execute(null)));

        panel.Children.Add(MakeActionBtn("↪", "重做 (Ctrl+Y)",
            () => Board?.RedoCommand.Execute(null)));

        panel.Children.Add(MakeSep());

        panel.Children.Add(MakeActionBtn("🗑", "清空白板",
            () => Board?.ClearPageCommand.Execute(null)));

        panel.Children.Add(MakeSep());

        // 选择形状后进入 Shape 工具，由用户在画布拖拽确定位置与大小（drag-to-create）
        var shapeMenu = new ContextMenu();
        foreach (var (type, label) in ShapeDefs)
        {
            var captured = type;
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => Board?.Toolbar.SetShapeTool(captured);
            shapeMenu.Items.Add(item);
        }

        var shapeBtn = new Button
        {
            Content = new TextBlock { Text = "形状▼", FontSize = 11 },
            Width = 48,
            Height = 34,
            Margin = new Thickness(2, 0, 2, 0),
            ToolTip = "插入基本形状",
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(0xEB, 0xEB, 0xEB)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
        };
        shapeBtn.Click += (_, _) =>
        {
            shapeMenu.PlacementTarget = shapeBtn;
            shapeMenu.Placement = PlacementMode.Bottom;
            shapeMenu.IsOpen = true;
        };
        panel.Children.Add(shapeBtn);

        // 插入图片
        panel.Children.Add(MakeActionBtn("🖼", "插入图片",
            () => Board?.InsertImageCommand.Execute(null)));

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = panel,
        };
        return (swatch, slider, thickText);
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnToolClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not Button { Tag: ToolType tool }) return;

        ICommand? cmd = tool switch
        {
            ToolType.Pen => ViewModel.SelectPenCommand,
            ToolType.Highlighter => ViewModel.SelectHighlighterCommand,
            ToolType.Marker => ViewModel.SelectMarkerCommand,
            ToolType.Eraser => ViewModel.SelectEraserCommand,
            ToolType.Select => ViewModel.SelectSelectCommand,
            ToolType.Hand => ViewModel.SelectHandCommand,
            _ => null,
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
        var activeBg = new SolidColorBrush(Color.FromRgb(0xCC, 0xE4, 0xFF));
        var activeBorder = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
        var normalBg = new SolidColorBrush(Color.FromRgb(0xEB, 0xEB, 0xEB));
        var normalBorder = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));

        foreach (var (tool, btn) in _toolButtons)
        {
            btn.Background = tool == active ? activeBg : normalBg;
            btn.BorderBrush = tool == active ? activeBorder : normalBorder;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Button MakeActionBtn(string icon, string tip, Action onClick)
    {
        var btn = new Button
        {
            Content = new TextBlock { Text = icon, FontSize = 14 },
            Width = 38,
            Height = 34,
            Margin = new Thickness(2, 0, 2, 0),
            ToolTip = tip,
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(0xEB, 0xEB, 0xEB)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private static UIElement MakeSep() => new Rectangle
    {
        Width = 1,
        Height = 28,
        Fill = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 8, 0),
    };

    private static Color ArgbToColor(uint argb)
        => Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb);
}