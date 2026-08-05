using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WhiteBoard.UI.Controls;

/// <summary>
/// 选中框浮动工具栏：Delete / 复制 / 置顶三个操作按钮。
/// 由 SelectionController 在选中集合变化时定位并显隐。
/// 以代码创建（无 XAML），挂载到 InkSurface._overlayCanvas 上。
/// </summary>
public sealed class SelectionToolbar : Canvas
{
    public event Action? DeleteClicked;

    public event Action? CopyClicked;

    public event Action? BringToFrontClicked;

    private const double BtnSize = 32;
    private const double Gap = 4;
    private const double Padding = 6;

    public SelectionToolbar()
    {
        var defs = new (string Icon, string Tip, Action Fire)[]
        {
            ("🗑", "删除",   () => DeleteClicked?.Invoke()),
            ("⎘", "复制",   () => CopyClicked?.Invoke()),
            ("⬆", "置顶",   () => BringToFrontClicked?.Invoke()),
        };

        double x = Padding;
        foreach (var (icon, tip, fire) in defs)
        {
            var localFire = fire;
            var btn = new Button
            {
                Content = icon,
                ToolTip = tip,
                Width = BtnSize,
                Height = BtnSize,
                FontSize = 14,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            btn.Click += (_, _) => localFire();
            SetLeft(btn, x);
            SetTop(btn, Padding);
            Children.Add(btn);
            x += BtnSize + Gap;
        }

        Width = x - Gap + Padding;
        Height = BtnSize + Padding * 2;
        Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255));
        Visibility = Visibility.Collapsed;

        // 圆角效果
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(210, 210, 210)),
            BorderThickness = new Thickness(1),
            Width = Width,
            Height = Height,
            IsHitTestVisible = false,
        };
        SetLeft(border, 0);
        SetTop(border, 0);
        Children.Insert(0, border); // 置于按钮下层（背景）
    }

    /// <summary>在 Canvas 坐标系中显示工具栏（左上角对齐目标点）。</summary>
    public void ShowAt(double left, double top)
    {
        SetLeft(this, left);
        SetTop(this, top);
        Visibility = Visibility.Visible;
    }

    public void Hide() => Visibility = Visibility.Collapsed;
}