using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace WhiteBoard.Framework.Hosting.Wpf.Localization;

/// <summary>XAML 本地化扩展：<c>Text="{loc:Loc MainWindow.Title}"</c>。</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (Application.Current?.Resources[WpfLocalizationBindingSource.ResourceKey] is not WpfLocalizationBindingSource source)
            return $"!{Key}!";
        return new Binding($"[{Key}]") { Source = source, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
    }
}
