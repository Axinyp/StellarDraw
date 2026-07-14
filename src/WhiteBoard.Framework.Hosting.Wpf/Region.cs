using System.Windows;

namespace WhiteBoard.Framework.Hosting.Wpf;

/// <summary>仅用于在 XAML 上标记区域名称；实际注册仍需通过 IRegionManager 显式完成。</summary>
public static class Region
{
    public static readonly DependencyProperty NameProperty = DependencyProperty.RegisterAttached(
        "Name", typeof(string), typeof(Region), new PropertyMetadata(null));

    public static void SetName(DependencyObject element, string? value) => element.SetValue(NameProperty, value);

    public static string? GetName(DependencyObject element) => (string?)element.GetValue(NameProperty);
}
