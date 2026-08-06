using Axinyp.Wpf.Hosting.Abstractions.Settings;

namespace Axinyp.Wpf.Hosting.Tests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"Axinyp-WpfHosting-Settings-{Guid.NewGuid():N}");

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

    [Fact]
    public async Task Get_WhenSettingsFileIsLocked_DegradesToDefaultsInsteadOfThrowing()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, """{"schemaVersion":1,"values":{"ui.theme":"dark"}}""");

        // 模拟杀软/同步工具锁定：独占打开且不共享读取。
        await using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var store = CreateStore();
            Assert.Equal("light", await store.GetAsync("ui.theme", "light"));
        }
    }

    [Fact]
    public async Task Set_WhenPersistFails_RollsBackMemoryAndCleansTempFiles()
    {
        var store = CreateStore();
        await store.SetAsync("ui.theme", "dark");

        var path = Path.Combine(_directory, "settings.json");
        await using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // 目标文件被锁定，原子替换失败：值不得残留在内存中被后续保存顺带写入。
            await Assert.ThrowsAnyAsync<Exception>(async () => await store.SetAsync("ui.theme", "blue"));
            Assert.Equal("dark", await store.GetAsync("ui.theme", "light"));

            await Assert.ThrowsAnyAsync<Exception>(async () => await store.RemoveAsync("ui.theme"));
            Assert.Equal("dark", await store.GetAsync("ui.theme", "light"));
        }

        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));

        // 锁释放后可正常继续持久化。
        await store.SetAsync("ui.theme", "blue");
        Assert.Equal("blue", await CreateStore().GetAsync("ui.theme", "light"));
    }

    [Fact]
    public async Task ConcurrentSetAndGet_AreThreadSafeAndAllValuesPersist()
    {
        var store = CreateStore();

        await Task.WhenAll(Enumerable.Range(0, 16).Select(index => Task.Run(async () =>
        {
            for (var round = 0; round < 5; round++)
            {
                await store.SetAsync($"key-{index}", $"value-{index}-{round}");
                _ = await store.GetAsync($"key-{index}", string.Empty);
            }
        })));

        var reloaded = CreateStore();
        for (var index = 0; index < 16; index++)
            Assert.Equal($"value-{index}-4", await reloaded.GetAsync($"key-{index}", string.Empty));
    }

    [Fact]
    public async Task Set_PreCancelledToken_ThrowsWithoutTouchingDisk()
    {
        var store = CreateStore();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await store.SetAsync("ui.theme", "dark", cancellation.Token));

        Assert.False(File.Exists(Path.Combine(_directory, "settings.json")));
    }

    private ISettingsStore CreateStore()
    {
        var services = new ServiceCollection();
        services.AddJsonSettings(options => options.DirectoryPath = _directory);
        return services.BuildServiceProvider().GetRequiredService<ISettingsStore>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
