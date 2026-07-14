using System.Globalization;
using WhiteBoard.Framework.Hosting.Settings;

namespace WhiteBoard.Framework.Hosting.Localization;

internal sealed class LocalizationService(LocalizationOptions options, ISettingsStore settings) : ILocalizationService
{
    private readonly object _gate = new();
    private CultureInfo _current = options.DefaultCulture;
    public CultureInfo CurrentCulture { get { lock (_gate) return _current; } }
    public event EventHandler? CultureChanged;

    public string GetString(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        foreach (var manager in options.ResourceManagers)
        {
            var value = manager.GetString(key, CurrentCulture);
            if (value is not null) return value;
        }
        return $"!{key}!";
    }

    public async ValueTask SetCultureAsync(CultureInfo culture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (!options.SupportedCultures.Any(c => Equals(c, culture)))
            throw new ArgumentException($"不支持文化 '{culture.Name}'。", nameof(culture));
        lock (_gate) _current = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        await settings.SetAsync(options.SettingsKey, culture.Name, cancellationToken).ConfigureAwait(false);
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }
}
