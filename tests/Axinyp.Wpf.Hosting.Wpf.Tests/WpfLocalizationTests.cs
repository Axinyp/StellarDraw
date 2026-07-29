using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Axinyp.Wpf.Hosting.Abstractions.Localization;
using Axinyp.Wpf.Hosting.Abstractions.Settings;
using Axinyp.Wpf.Hosting.Abstractions.Startup;
using Axinyp.Wpf.Hosting.Wpf.Extensions;
using Axinyp.Wpf.Hosting.Wpf.Localization;

namespace Axinyp.Wpf.Hosting.Wpf.Tests;

public sealed class WpfLocalizationTests
{
    [Fact]
    public async Task StartupTask_PublishesBindingSourceAndCultureChangeRefreshesIndexerBindings()
    {
        await Sta.RunAsync(async () =>
        {
            var localization = new TestLocalizationService();
            var services = new ServiceCollection();
            services.AddSingleton<ILocalizationService>(localization);
            services.AddWpfLocalizationBindings();
            using var provider = services.BuildServiceProvider();
            var source = provider.GetRequiredService<WpfLocalizationBindingSource>();
            var changes = new List<string?>();
            source.PropertyChanged += (_, eventArgs) => changes.Add(eventArgs.PropertyName);

            var startup = Assert.Single(provider.GetServices<IStartupTask>());
            await startup.ExecuteAsync(CancellationToken.None);

            Assert.Same(source, Application.Current.Resources[WpfLocalizationBindingSource.ResourceKey]);
            Assert.Equal("English", source["Greeting"]);
            Assert.Contains("Item[]", changes);

            await localization.SetCultureAsync(CultureInfo.GetCultureInfo("zh-CN"));

            Assert.Equal("你好", source["Greeting"]);
            Assert.Equal(2, changes.Count(propertyName => propertyName == "Item[]"));
            Application.Current.Resources.Remove(WpfLocalizationBindingSource.ResourceKey);
        });
    }

    [Fact]
    public async Task LocExtension_ReturnsFallbackBeforeStartupAndBindingAfterSourceIsPublished()
    {
        await Sta.RunAsync(async () =>
        {
            Application.Current.Resources.Remove(WpfLocalizationBindingSource.ResourceKey);
            var extension = new LocExtension("Greeting");

            Assert.Equal("!Greeting!", extension.ProvideValue(null!));

            var localization = new TestLocalizationService();
            var services = new ServiceCollection();
            services.AddSingleton<ILocalizationService>(localization);
            services.AddWpfLocalizationBindings();
            using var provider = services.BuildServiceProvider();
            var startup = Assert.Single(provider.GetServices<IStartupTask>());
            await startup.ExecuteAsync(CancellationToken.None);

            var binding = Assert.IsType<System.Windows.Data.Binding>(extension.ProvideValue(null!));
            var target = new TextBlock();
            target.SetBinding(TextBlock.TextProperty, binding);
            Assert.Equal("English", target.Text);

            await localization.SetCultureAsync(CultureInfo.GetCultureInfo("zh-CN"));

            Assert.Equal("你好", target.Text);
            Application.Current.Resources.Remove(WpfLocalizationBindingSource.ResourceKey);
        });
    }

    private sealed class TestLocalizationService : ILocalizationService
    {
        private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _strings = new(StringComparer.Ordinal)
        {
            ["en-US"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Greeting"] = "English" },
            ["zh-CN"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Greeting"] = "你好" }
        };

        public CultureInfo CurrentCulture { get; private set; } = CultureInfo.GetCultureInfo("en-US");
        public event EventHandler? CultureChanged;

        public string GetString(string key)
            => _strings[CurrentCulture.Name].TryGetValue(key, out var value) ? value : $"!{key}!";

        public ValueTask SetCultureAsync(CultureInfo culture, CancellationToken cancellationToken = default)
        {
            CurrentCulture = culture;
            CultureChanged?.Invoke(this, EventArgs.Empty);
            return ValueTask.CompletedTask;
        }
    }
}
