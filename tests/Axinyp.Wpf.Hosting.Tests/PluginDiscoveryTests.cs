namespace Axinyp.Wpf.Hosting.Tests;

public sealed class PluginDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"Axinyp-WpfHosting-Plugins-{Guid.NewGuid():N}");

    [Fact]
    public void Build_SkipsInvalidPluginWithoutPreventingHostConstruction()
    {
        var directory = CreatePluginDirectory("invalid");
        File.WriteAllText(Path.Combine(directory, "plugin.json"), "{ invalid json");

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddModularHosting().AddStartupPluginsFromDirectory(_root);

        using var host = builder.BuildModularHost();
        var failure = Assert.Single(host.Services.GetRequiredService<IPluginCatalog>().Failures);

        Assert.Equal(PluginLoadFailureKind.ManifestInvalid, failure.Kind);
        Assert.Equal("plugin.discovery.ManifestInvalid", failure.Metadata?.DiagnosticCode);
        Assert.Equal(1040, failure.Metadata?.EventId);
        Assert.Equal(typeof(System.Text.Json.JsonException).FullName, failure.Metadata?.ExceptionType);
        Assert.Empty(host.Services.GetRequiredService<IPluginCatalog>().Plugins);
    }

    [Fact]
    public void Build_ReportsMissingPluginDependency()
    {
        var directory = CreatePluginDirectory("missing-dependency");
        File.WriteAllText(Path.Combine(directory, "plugin.json"), """
        { "Id": "sample.missing", "Version": "1.0.0", "EntryAssembly": "sample.dll", "Dependencies": ["not.installed"] }
        """);

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddModularHosting().AddStartupPluginsFromDirectory(_root);

        using var host = builder.BuildModularHost();
        var failure = Assert.Single(host.Services.GetRequiredService<IPluginCatalog>().Failures);

        Assert.Equal("sample.missing", failure.Id);
        Assert.Equal(PluginLoadFailureKind.MissingDependency, failure.Kind);
        Assert.Equal("not.installed", failure.Metadata?.DependencyId);
        Assert.Equal("sample.dll", failure.Metadata?.EntryAssembly);
    }

    [Fact]
    public void Build_ReportsDuplicatePluginIdsCaseInsensitively()
    {
        WriteManifest(CreatePluginDirectory("first"), "sample.plugin");
        WriteManifest(CreatePluginDirectory("second"), "SAMPLE.PLUGIN");

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddModularHosting().AddStartupPluginsFromDirectory(_root);

        using var host = builder.BuildModularHost();
        var failures = host.Services.GetRequiredService<IPluginCatalog>().Failures;

        Assert.Equal(2, failures.Count);
        Assert.All(failures, failure => Assert.Equal(PluginLoadFailureKind.DuplicatePluginId, failure.Kind));
    }

    [Fact]
    public void Build_LogsSafeStructuredPluginDiscoveryFailure()
    {
        var directory = CreatePluginDirectory("missing-entry-assembly");
        WriteManifest(directory, "sample.missing-entry");
        var loggerProvider = new RecordingLoggerProvider();
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(loggerProvider);
        builder.AddModularHosting().AddStartupPluginsFromDirectory(_root);

        using var host = builder.BuildModularHost();

        var entry = Assert.Single(loggerProvider.Entries, entry =>
            entry.EventId.Id == 1040 && entry.EventId.Name == "DiscoveryFailure");
        Assert.Equal(LogLevel.Warning, entry.LogLevel);
        Assert.IsAssignableFrom<InvalidOperationException>(entry.Exception);
        Assert.Equal(directory, entry.Properties["PluginDirectory"]);
        Assert.Equal("sample.missing-entry", entry.Properties["PluginId"]);
        Assert.Equal(PluginLoadFailureKind.EntryAssemblyMissing, entry.Properties["FailureKind"]);
        Assert.Equal("sample.dll", entry.Properties["EntryAssembly"]);
        Assert.EndsWith("PluginEntryAssemblyMissingException", entry.Properties["ExceptionType"]?.ToString());
    }

    [Fact]
    public void FailureModels_DoNotExposeExceptionInstances()
    {
        Assert.DoesNotContain(typeof(PluginLoadFailure).GetProperties(), property =>
            typeof(Exception).IsAssignableFrom(property.PropertyType));
        Assert.DoesNotContain(typeof(PluginLoadFailureMetadata).GetProperties(), property =>
            typeof(Exception).IsAssignableFrom(property.PropertyType));
    }

    [Fact]
    public void Build_EntryAssemblyWithoutModules_ReportsModuleDiscoveryFailed()
    {
        var directory = CreatePluginDirectory("no-modules");
        File.Copy(typeof(IModule).Assembly.Location, Path.Combine(directory, "sample.dll"));
        WriteManifest(directory, "sample.nomodules");

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddModularHosting().AddStartupPluginsFromDirectory(_root);

        using var host = builder.BuildModularHost();
        var failure = Assert.Single(host.Services.GetRequiredService<IPluginCatalog>().Failures);

        Assert.Equal(PluginLoadFailureKind.ModuleDiscoveryFailed, failure.Kind);
        Assert.Empty(host.Services.GetRequiredService<IPluginCatalog>().Plugins);
    }

    [Fact]
    public void Build_SameDirectoryScannedTwiceInOneCall_LoadsPluginOnceWithoutConflict()
    {
        CreateLoadablePluginDirectory("real", "sample.real");

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddModularHosting().AddStartupPlugins(options =>
        {
            options.PluginDirectories.Add(_root);
            options.PluginDirectories.Add(_root);
        });

        using var host = builder.BuildModularHost();
        var catalog = host.Services.GetRequiredService<IPluginCatalog>();

        // 重叠的根目录扫到同一物理插件目录是配置冗余，不是 ID 冲突。
        Assert.Single(catalog.Plugins);
        Assert.Empty(catalog.Failures);
    }

    [Fact]
    public void Build_DuplicatePluginIdAcrossCalls_KeepsFirstAndRecordsFailure()
    {
        CreateLoadablePluginDirectory("real", "sample.real");

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddModularHosting()
            .AddStartupPluginsFromDirectory(_root)
            .AddStartupPluginsFromDirectory(_root);

        using var host = builder.BuildModularHost();
        var catalog = host.Services.GetRequiredService<IPluginCatalog>();

        Assert.Single(catalog.Plugins);
        var failure = Assert.Single(catalog.Failures);
        Assert.Equal(PluginLoadFailureKind.DuplicatePluginId, failure.Kind);

        // 重复副本的模块不得二次注册：模块目录中只有首个副本的模块。
        Assert.Single(host.Services.GetRequiredService<IModuleCatalog>().Modules);
    }

    [Fact]
    public async Task BuildModularHost_WithPlugins_SupportsAsyncDispose()
    {
        CreateLoadablePluginDirectory("real", "sample.real");

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddModularHosting().AddStartupPluginsFromDirectory(_root);

        var host = builder.BuildModularHost();
        var asyncDisposable = Assert.IsAssignableFrom<IAsyncDisposable>(host);

        await asyncDisposable.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => host.Services);
    }

    private string CreateLoadablePluginDirectory(string name, string id)
    {
        var directory = CreatePluginDirectory(name);
        File.Copy(typeof(PluginDiscoveryTests).Assembly.Location, Path.Combine(directory, "sample.dll"));
        WriteManifest(directory, id);
        return directory;
    }

    private string CreatePluginDirectory(string name)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void WriteManifest(string directory, string id)
    {
        File.WriteAllText(Path.Combine(directory, "plugin.json"), $$"""
        { "Id": "{{id}}", "Version": "1.0.0", "EntryAssembly": "sample.dll" }
        """);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;

        // 可回收 ALC 在 GC 前仍映射着插件 DLL；清理竞态不应使测试失败。
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(_root, recursive: true);
                return;
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
    }
}
