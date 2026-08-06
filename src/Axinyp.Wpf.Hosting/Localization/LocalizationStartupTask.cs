using System.Globalization;
using Axinyp.Wpf.Hosting.Abstractions.Startup;
using Axinyp.Wpf.Hosting.Abstractions.Settings;

namespace Axinyp.Wpf.Hosting.Localization;

/// <summary>
/// Host 启动时从用户偏好恢复 culture；无效值安全回退到默认文化。
/// 偏好读取或持久化失败只记录警告——用户偏好不可用不构成宿主启动的致命错误。
/// </summary>
internal sealed class LocalizationStartupTask(ILocalizationService localization, LocalizationOptions options,
    ISettingsStore settings, ILogger<LocalizationStartupTask> logger) : IStartupTask
{
    private static readonly EventId LocalizationRestoreFailed = new(1051, nameof(LocalizationRestoreFailed));

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var saved = await settings.GetAsync<string?>(options.SettingsKey, null, cancellationToken).ConfigureAwait(false);
            var culture = options.SupportedCultures.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, saved, StringComparison.OrdinalIgnoreCase)) ?? options.DefaultCulture;
            await localization.SetCultureAsync(culture, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(LocalizationRestoreFailed, exception,
                "恢复本地化偏好失败，应用将以进程默认文化继续启动。");
        }
    }
}
