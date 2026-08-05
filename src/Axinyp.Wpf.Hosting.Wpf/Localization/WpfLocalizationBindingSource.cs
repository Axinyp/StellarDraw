using System.ComponentModel;
using System.Diagnostics;
using Axinyp.Wpf.Hosting.Abstractions.Localization;
using Axinyp.Wpf.Hosting.Abstractions.Wpf;

namespace Axinyp.Wpf.Hosting.Wpf.Localization;

/// <summary>供 LocExtension 使用的动态绑定源；文化切换时刷新所有 indexer 绑定。</summary>
internal sealed class WpfLocalizationBindingSource(
    ILocalizationService localization,
    IUiDispatcher uiDispatcher) : INotifyPropertyChanged
{
    public const string ResourceKey = "Axinyp.Wpf.Hosting.Wpf.LocalizationSource";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => localization.GetString(key);

    public Task NotifyCultureChangedAsync()
    {
        var dispatcher = uiDispatcher.GetApplicationDispatcher();
        return uiDispatcher.IsShutdown(dispatcher)
            ? Task.CompletedTask
            : uiDispatcher.InvokeAsync(dispatcher, Raise);
    }

    public void NotifyCultureChanged() => ObserveNotification(NotifyCultureChangedAsync());

    private static void ObserveNotification(Task notification)
    {
        _ = notification.ContinueWith(
            completed => Trace.TraceError("WPF 本地化绑定刷新失败：{0}", completed.Exception),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
}
