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

    /// <summary>
    /// 串行化整个「应用字典 + 持久化 + 失败回滚 + 事件通知」临界区。
    /// 两次 ApplyAsync 交错时，后者会移除前者的字典，前者的回滚将把
    /// MergedDictionaries 弄成同时挂两份主题、_current 与实际生效不符。
    /// </summary>
    private readonly SemaphoreSlim _applyGate = new(1, 1);

    public ThemeDescriptor CurrentTheme => _current ?? throw new InvalidOperationException("尚未应用主题。");
    public IReadOnlyList<ThemeDescriptor> Themes => options.Themes.ToList();
    public event EventHandler? ThemeChanged;

    public async ValueTask ApplyAsync(string themeId, CancellationToken cancellationToken = default)
    {
        var descriptor = options.Themes.FirstOrDefault(theme => string.Equals(theme.Id, themeId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"未注册主题 '{themeId}'。", nameof(themeId));
        var dispatcher = uiDispatcher.GetApplicationDispatcher();

        await _applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ApplyLockedAsync(descriptor, dispatcher, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _applyGate.Release();
        }
    }

    private async ValueTask ApplyLockedAsync(
        ThemeDescriptor descriptor,
        System.Windows.Threading.Dispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        ResourceDictionary? previousDictionary = null;
        ThemeDescriptor? previousTheme = null;
        ResourceDictionary? appliedDictionary = null;
        var previousDictionaryIndex = -1;

        await uiDispatcher.InvokeAsync(dispatcher, () =>
        {
            appliedDictionary = new ResourceDictionary { Source = descriptor.ResourceUri };
            var merged = Application.Current.Resources.MergedDictionaries;
            previousDictionary = _appliedDictionary;
            previousTheme = _current;
            if (previousDictionary is not null)
            {
                previousDictionaryIndex = merged.IndexOf(previousDictionary);
                merged.Remove(previousDictionary);
            }

            try
            {
                merged.Add(appliedDictionary);
            }
            catch
            {
                if (previousDictionary is not null && !merged.Contains(previousDictionary))
                {
                    var restoreIndex = previousDictionaryIndex < 0
                        ? merged.Count
                        : Math.Min(previousDictionaryIndex, merged.Count);
                    merged.Insert(restoreIndex, previousDictionary);
                }

                throw;
            }

            _appliedDictionary = appliedDictionary;
            _current = descriptor;
        }).ConfigureAwait(false);

        try
        {
            await settings.SetAsync(options.SettingsKey, descriptor.Id, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await uiDispatcher.InvokeAsync(dispatcher, () =>
            {
                var merged = Application.Current.Resources.MergedDictionaries;
                if (appliedDictionary is not null)
                    merged.Remove(appliedDictionary);
                if (previousDictionary is not null && !merged.Contains(previousDictionary))
                {
                    var restoreIndex = previousDictionaryIndex < 0
                        ? merged.Count
                        : Math.Min(previousDictionaryIndex, merged.Count);
                    merged.Insert(restoreIndex, previousDictionary);
                }

                _appliedDictionary = previousDictionary;
                _current = previousTheme;
            }).ConfigureAwait(false);
            throw;
        }

        await uiDispatcher.InvokeAsync(dispatcher, () => ThemeChanged?.Invoke(this, EventArgs.Empty)).ConfigureAwait(false);
    }
}
