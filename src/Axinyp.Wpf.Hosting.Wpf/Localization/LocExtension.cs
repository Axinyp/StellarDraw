using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace Axinyp.Wpf.Hosting.Wpf.Localization;

/// <summary>XAML 本地化扩展：<c>Text="{loc:Loc MainWindow.Title}"</c>。</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension(string key) : MarkupExtension
{
    /// <summary>
    /// 要查找的资源键。
    /// </summary>
    public string Key { get; set; } = key;

    /// <summary>
    /// 为 XAML 绑定提供本地化值。
    /// </summary>
    /// <param name="serviceProvider">XAML 服务提供程序。</param>
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (Application.Current?.Resources[WpfLocalizationBindingSource.ResourceKey] is not WpfLocalizationBindingSource source)
            return $"!{Key}!";
        return new Binding($"[{Key}]") { Source = source, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
    }
}
