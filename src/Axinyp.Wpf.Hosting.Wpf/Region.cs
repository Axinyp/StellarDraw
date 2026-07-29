using System.Windows;

namespace Axinyp.Wpf.Hosting.Wpf;

/// <summary>仅用于在 XAML 上标记区域名称；实际注册仍需通过 IRegionManager 显式完成。</summary>
public static class Region
{
    /// <summary>
    /// 区域名称附加属性。
    /// </summary>
    public static readonly DependencyProperty NameProperty = DependencyProperty.RegisterAttached(
        "Name", typeof(string), typeof(Region), new PropertyMetadata(null));

    /// <summary>
    /// 设置元素上的区域名称。
    /// </summary>
    /// <param name="element">目标元素。</param>
    /// <param name="value">区域名称。</param>
    public static void SetName(DependencyObject element, string? value) => element.SetValue(NameProperty, value);

    /// <summary>
    /// 读取元素上的区域名称。
    /// </summary>
    /// <param name="element">目标元素。</param>
    public static string? GetName(DependencyObject element) => (string?)element.GetValue(NameProperty);
}
