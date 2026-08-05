using System.Windows;
using Axinyp.Wpf.Hosting.Abstractions.Localization;
using Axinyp.Wpf.Hosting.Abstractions.Startup;
using Axinyp.Wpf.Hosting.Abstractions.Wpf;

namespace Axinyp.Wpf.Hosting.Wpf.Localization;

/// <summary>把动态本地化绑定源挂到 Application.Resources，并转发文化变更。</summary>
internal sealed class WpfLocalizationStartupTask(
    WpfLocalizationBindingSource source,
    ILocalizationService localization,
    IUiDispatcher uiDispatcher) : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dispatcher = uiDispatcher.GetApplicationDispatcher();
        await uiDispatcher.InvokeAsync(dispatcher, () =>
        {
            Application.Current.Resources[WpfLocalizationBindingSource.ResourceKey] = source;
            localization.CultureChanged += (_, _) => source.NotifyCultureChanged();
        }).ConfigureAwait(false);
        await source.NotifyCultureChangedAsync().ConfigureAwait(false);
    }
}
