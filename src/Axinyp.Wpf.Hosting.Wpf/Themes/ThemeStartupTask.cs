using Axinyp.Wpf.Hosting.Abstractions.Settings;
using Axinyp.Wpf.Hosting.Abstractions.Startup;

namespace Axinyp.Wpf.Hosting.Wpf.Themes;

/// <summary>
/// Host 启动时恢复用户选择的主题；不存在或无效时应用默认主题。
/// 偏好读取或主题应用失败只记录警告——主题不可用不构成宿主启动的致命错误。
/// </summary>
internal sealed class ThemeStartupTask(IThemeService themes, ThemeOptions options, ISettingsStore settings,
    ILogger<ThemeStartupTask> logger) : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var saved = await settings.GetAsync<string?>(options.SettingsKey, null, cancellationToken).ConfigureAwait(false);
            var selected = options.Themes.FirstOrDefault(theme =>
                string.Equals(theme.Id, saved, StringComparison.OrdinalIgnoreCase))
                ?? options.Themes.FirstOrDefault(theme => theme.IsDefault)
                ?? options.Themes[0];
            await themes.ApplyAsync(selected.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(WpfHostingEventIds.ThemeRestoreFailed, exception,
                "恢复主题偏好失败，应用将在未应用主题字典的状态下继续启动。");
        }
    }
}
