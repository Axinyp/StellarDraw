using System.Windows;
using WhiteBoard.Framework.Hosting.Localization;
using WhiteBoard.Framework.Hosting.Startup;

namespace WhiteBoard.Framework.Hosting.Wpf.Localization;

/// <summary>把动态本地化绑定源挂到 Application.Resources，并转发文化变更。</summary>
internal sealed class WpfLocalizationStartupTask(WpfLocalizationBindingSource source, ILocalizationService localization) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var dispatcher = Application.Current?.Dispatcher ?? throw new InvalidOperationException("没有可用的 WPF Application。");
        return dispatcher.InvokeAsync(() =>
        {
            Application.Current.Resources[WpfLocalizationBindingSource.ResourceKey] = source;
            localization.CultureChanged += (_, _) => source.NotifyCultureChanged();
            source.NotifyCultureChanged();
        }).Task;
    }
}
