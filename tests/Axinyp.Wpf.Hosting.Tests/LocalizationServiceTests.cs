using System.Globalization;
using Axinyp.Wpf.Hosting.Abstractions.Localization;
using Axinyp.Wpf.Hosting.Abstractions.Settings;

namespace Axinyp.Wpf.Hosting.Tests;

public sealed class LocalizationServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"Axinyp-WpfHosting-Localization-{Guid.NewGuid():N}");

    [Fact]
    public async Task SetCulture_PersistsSupportedCultureAndRaisesChange()
    {
        var services = new ServiceCollection();
        services.AddJsonSettings(options => options.DirectoryPath = _directory);
        services.AddResxLocalization(options =>
        {
            options.DefaultCulture = CultureInfo.GetCultureInfo("en-US");
            options.SupportedCultures.Add(CultureInfo.GetCultureInfo("en-US"));
            options.SupportedCultures.Add(CultureInfo.GetCultureInfo("zh-CN"));
        });
        using var provider = services.BuildServiceProvider();
        var localization = provider.GetRequiredService<ILocalizationService>();
        var changed = false;
        localization.CultureChanged += (_, _) => changed = true;

        await localization.SetCultureAsync(CultureInfo.GetCultureInfo("zh-CN"));

        Assert.Equal("zh-CN", localization.CurrentCulture.Name);
        Assert.Equal("zh-CN", await provider.GetRequiredService<ISettingsStore>().GetAsync("ui.culture", ""));
        Assert.True(changed);
    }

    [Fact]
    public async Task SetCulture_RejectsUnsupportedCulture()
    {
        var services = new ServiceCollection();
        services.AddJsonSettings(options => options.DirectoryPath = _directory);
        services.AddResxLocalization(options =>
        {
            options.DefaultCulture = CultureInfo.GetCultureInfo("en-US");
            options.SupportedCultures.Add(CultureInfo.GetCultureInfo("en-US"));
        });
        using var provider = services.BuildServiceProvider();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await provider.GetRequiredService<ILocalizationService>().SetCultureAsync(CultureInfo.GetCultureInfo("zh-CN")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
