using WhiteBoard.Framework.Hosting.Settings;
using WhiteBoard.Framework.Hosting.Startup;

namespace WhiteBoard.Framework.Hosting.Wpf.Themes;

/// <summary>Host 启动时恢复用户选择的主题；不存在或无效时应用默认主题。</summary>
internal sealed class ThemeStartupTask(IThemeService themes, ThemeOptions options, ISettingsStore settings) : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var saved = await settings.GetAsync<string?>(options.SettingsKey, null, cancellationToken).ConfigureAwait(false);
        var selected = options.Themes.FirstOrDefault(theme =>
            string.Equals(theme.Id, saved, StringComparison.OrdinalIgnoreCase))
            ?? options.Themes.FirstOrDefault(theme => theme.IsDefault)
            ?? options.Themes[0];
        await themes.ApplyAsync(selected.Id, cancellationToken).ConfigureAwait(false);
    }
}
