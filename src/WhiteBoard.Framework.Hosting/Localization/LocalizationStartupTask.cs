using System.Globalization;
using WhiteBoard.Framework.Hosting.Startup;
using WhiteBoard.Framework.Hosting.Settings;

namespace WhiteBoard.Framework.Hosting.Localization;

/// <summary>Host 启动时从用户偏好恢复 culture；无效值安全回退到默认文化。</summary>
internal sealed class LocalizationStartupTask(ILocalizationService localization, LocalizationOptions options,
    ISettingsStore settings) : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var saved = await settings.GetAsync<string?>(options.SettingsKey, null, cancellationToken).ConfigureAwait(false);
        var culture = options.SupportedCultures.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, saved, StringComparison.OrdinalIgnoreCase)) ?? options.DefaultCulture;
        await localization.SetCultureAsync(culture, cancellationToken).ConfigureAwait(false);
    }
}
