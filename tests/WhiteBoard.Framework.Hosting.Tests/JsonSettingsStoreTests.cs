using WhiteBoard.Framework.Hosting.Settings;

namespace WhiteBoard.Framework.Hosting.Tests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"StellarDraw-Settings-{Guid.NewGuid():N}");

    [Fact]
    public async Task SetGetAndRemove_RoundTripsPreferenceAcrossStoreInstances()
    {
        var first = CreateStore();
        await first.SetAsync("ui.theme", "dark");
        Assert.Equal("dark", await first.GetAsync("ui.theme", "light"));

        var second = CreateStore();
        Assert.Equal("dark", await second.GetAsync("ui.theme", "light"));
        Assert.True(await second.RemoveAsync("ui.theme"));
        Assert.Equal("light", await second.GetAsync("ui.theme", "light"));
    }

    [Theory]
    [InlineData("api.token")]
    [InlineData("credentials.password")]
    [InlineData("secret.value")]
    public async Task Set_RejectsSensitivePreferenceKeys(string key)
    {
        var store = CreateStore();
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.SetAsync(key, "value"));
    }

    [Fact]
    public async Task Get_CorruptFile_FallsBackToDefaultAndQuarantinesFile()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "settings.json"), "{ invalid json");

        var store = CreateStore();
        Assert.Equal("light", await store.GetAsync("ui.theme", "light"));
        Assert.NotEmpty(Directory.GetFiles(_directory, "settings.json.bad.*"));
    }

    private ISettingsStore CreateStore()
    {
        var services = new ServiceCollection();
        services.AddWhiteBoardSettings(options => options.DirectoryPath = _directory);
        return services.BuildServiceProvider().GetRequiredService<ISettingsStore>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
