using System.Windows;
using Axinyp.Wpf.Hosting.Abstractions.Settings;

namespace Axinyp.Wpf.Hosting.Wpf.Themes;

internal sealed class ResourceDictionaryThemeService(
    ThemeOptions options,
    ISettingsStore settings,
    IUiDispatcher uiDispatcher) : IThemeService
{
    private ResourceDictionary? _appliedDictionary;
    private ThemeDescriptor? _current;
    public ThemeDescriptor CurrentTheme => _current ?? throw new InvalidOperationException("尚未应用主题。");
    public IReadOnlyList<ThemeDescriptor> Themes => options.Themes.ToList();
    public event EventHandler? ThemeChanged;

    public async ValueTask ApplyAsync(string themeId, CancellationToken cancellationToken = default)
    {
        var descriptor = options.Themes.FirstOrDefault(theme => string.Equals(theme.Id, themeId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"未注册主题 '{themeId}'。", nameof(themeId));
        var dispatcher = uiDispatcher.GetApplicationDispatcher();
        await uiDispatcher.InvokeAsync(dispatcher, () =>
        {
            var dictionary = new ResourceDictionary { Source = descriptor.ResourceUri };
            var merged = Application.Current.Resources.MergedDictionaries;
            if (_appliedDictionary is not null) merged.Remove(_appliedDictionary);
            merged.Add(dictionary);
            _appliedDictionary = dictionary;
            _current = descriptor;
        }).ConfigureAwait(false);

        await settings.SetAsync(options.SettingsKey, descriptor.Id, cancellationToken).ConfigureAwait(false);
        await uiDispatcher.InvokeAsync(dispatcher, () => ThemeChanged?.Invoke(this, EventArgs.Empty)).ConfigureAwait(false);
    }
}
