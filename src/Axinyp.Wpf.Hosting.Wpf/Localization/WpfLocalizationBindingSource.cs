using System.ComponentModel;
using Axinyp.Wpf.Hosting.Abstractions.Localization;

namespace Axinyp.Wpf.Hosting.Wpf.Localization;

/// <summary>供 LocExtension 使用的动态绑定源；文化切换时刷新所有 indexer 绑定。</summary>
internal sealed class WpfLocalizationBindingSource(ILocalizationService localization) : INotifyPropertyChanged
{
    public const string ResourceKey = "Axinyp.Wpf.Hosting.Wpf.LocalizationSource";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => localization.GetString(key);

    public void NotifyCultureChanged()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
        if (dispatcher.CheckAccess()) Raise();
        else _ = dispatcher.InvokeAsync(Raise);
    }

    private void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
}
