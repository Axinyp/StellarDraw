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
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
