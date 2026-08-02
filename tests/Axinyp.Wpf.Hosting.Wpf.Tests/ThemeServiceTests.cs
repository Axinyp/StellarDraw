using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Axinyp.Wpf.Hosting.Abstractions.Settings;
using Axinyp.Wpf.Hosting.Abstractions.Startup;
using Axinyp.Wpf.Hosting.Abstractions.Wpf.Themes;
using Axinyp.Wpf.Hosting.Wpf.Extensions;

namespace Axinyp.Wpf.Hosting.Wpf.Tests;

public sealed class ThemeServiceTests
{
    private static readonly Uri LightThemeUri = new(
        "pack://application:,,,/Axinyp.Wpf.Hosting.Wpf.Tests;component/Themes/Light.xaml");
    private static readonly Uri DarkThemeUri = new(
        "pack://application:,,,/Axinyp.Wpf.Hosting.Wpf.Tests;component/Themes/Dark.xaml");

    [Fact]
    public async Task ApplyAsync_ReplacesMergedDictionaryPersistsThemeAndRaisesChanged()
    {
        await Sta.RunAsync(async () =>
        {
            var settings = new MemorySettingsStore();
            var services = CreateServices(settings);
            using var provider = services.BuildServiceProvider();
            var themes = provider.GetRequiredService<IThemeService>();
            var initialCount = Application.Current.Resources.MergedDictionaries.Count;
            var changed = 0;
            themes.ThemeChanged += (_, _) => changed++;

            await themes.ApplyAsync("light");

            var lightDictionary = Assert.Single(Application.Current.Resources.MergedDictionaries.Skip(initialCount));
            Assert.Equal("Light", lightDictionary["TestTheme"]);
            Assert.Equal("light", themes.CurrentTheme.Id);
            Assert.Equal("light", await settings.GetAsync<string?>("ui.theme", null));

            await themes.ApplyAsync("dark");

            var remaining = Application.Current.Resources.MergedDictionaries.Skip(initialCount).ToList();
            var darkDictionary = Assert.Single(remaining);
            Assert.NotSame(lightDictionary, darkDictionary);
            Assert.Equal("Dark", darkDictionary["TestTheme"]);
            Assert.Equal("dark", themes.CurrentTheme.Id);
            Assert.Equal("dark", await settings.GetAsync<string?>("ui.theme", null));
            Assert.Equal(2, changed);

            Application.Current.Resources.MergedDictionaries.Remove(darkDictionary);
        });
    }

    [Fact]
    public async Task StartupTask_RestoresSavedThemeAndFallsBackWhenSavedValueIsInvalid()
    {
        await Sta.RunAsync(async () =>
        {
            var savedSettings = new MemorySettingsStore();
            await savedSettings.SetAsync("ui.theme", "dark");
            using (var provider = CreateServices(savedSettings).BuildServiceProvider())
            {
                var dictionary = await ExecuteStartupAsync(provider);
                Assert.Equal("dark", provider.GetRequiredService<IThemeService>().CurrentTheme.Id);
                Application.Current.Resources.MergedDictionaries.Remove(dictionary);
            }

            var invalidSettings = new MemorySettingsStore();
            await invalidSettings.SetAsync("ui.theme", "missing");
            using (var provider = CreateServices(invalidSettings).BuildServiceProvider())
            {
                var dictionary = await ExecuteStartupAsync(provider);
                Assert.Equal("light", provider.GetRequiredService<IThemeService>().CurrentTheme.Id);
                Assert.Equal("light", await invalidSettings.GetAsync<string?>("ui.theme", null));
                Application.Current.Resources.MergedDictionaries.Remove(dictionary);
            }
        });
    }

    [Fact]
    public async Task ApplyAsync_WhenDispatcherIsUnavailable_DoesNotPersistTheme()
    {
        await Sta.RunAsync(async () =>
        {
            var settings = new MemorySettingsStore();
            var services = CreateServices(settings);
            services.AddSingleton<IUiDispatcher>(new RejectingUiDispatcher(Application.Current.Dispatcher));
            using var provider = services.BuildServiceProvider();
            var themes = provider.GetRequiredService<IThemeService>();

            await Assert.ThrowsAsync<InvalidOperationException>(() => themes.ApplyAsync("light").AsTask());

            Assert.Null(await settings.GetAsync<string?>("ui.theme", null));
        });
    }

    [Fact]
    public async Task ApplyAsync_RaisesThemeChangedOnApplicationDispatcher()
    {
        await Sta.RunAsync(async () =>
        {
            var settings = new MemorySettingsStore();
            using var provider = CreateServices(settings).BuildServiceProvider();
            var themes = provider.GetRequiredService<IThemeService>();
            var initialCount = Application.Current.Resources.MergedDictionaries.Count;
            var eventWasOnDispatcher = false;
            themes.ThemeChanged += (_, _) => eventWasOnDispatcher = Application.Current.Dispatcher.CheckAccess();

            await themes.ApplyAsync("light");

            Assert.True(eventWasOnDispatcher);
            var dictionary = Assert.Single(Application.Current.Resources.MergedDictionaries.Skip(initialCount));
            Application.Current.Resources.MergedDictionaries.Remove(dictionary);
        });
    }

    private static ServiceCollection CreateServices(MemorySettingsStore settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISettingsStore>(settings);
        services.AddWpfThemes(options =>
        {
            options.Themes.Add(new ThemeDescriptor("light", "Light", LightThemeUri, true));
            options.Themes.Add(new ThemeDescriptor("dark", "Dark", DarkThemeUri));
        });
        return services;
    }

    private static async Task<ResourceDictionary> ExecuteStartupAsync(ServiceProvider provider)
    {
        var initialCount = Application.Current.Resources.MergedDictionaries.Count;
        var startup = Assert.Single(provider.GetServices<IStartupTask>());
        await startup.ExecuteAsync(CancellationToken.None);
        return Assert.Single(Application.Current.Resources.MergedDictionaries.Skip(initialCount));
    }

    private sealed class MemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

        public ValueTask<T> GetAsync<T>(string key, T defaultValue, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_values.TryGetValue(key, out var value) && value is T typed ? typed : defaultValue);

        public ValueTask SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_values.Remove(key));
    }
}
