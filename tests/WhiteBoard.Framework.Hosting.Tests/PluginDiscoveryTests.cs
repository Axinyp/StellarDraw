using WhiteBoard.Framework.Hosting.Plugins;

namespace WhiteBoard.Framework.Hosting.Tests;

public sealed class PluginDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"StellarDraw-Plugins-{Guid.NewGuid():N}");

    [Fact]
    public void Build_SkipsInvalidPluginWithoutPreventingHostConstruction()
    {
        var directory = CreatePluginDirectory("invalid");
        File.WriteAllText(Path.Combine(directory, "plugin.json"), "{ invalid json");

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddWhiteBoardFramework().AddStartupPluginsFromDirectory(_root);

        using var host = builder.BuildWhiteBoardFrameworkHost();
        var catalog = host.Services.GetRequiredService<IPluginCatalog>();

        var failure = Assert.Single(catalog.Failures);
        Assert.Equal(PluginLoadFailureKind.ManifestInvalid, failure.Kind);
        Assert.Empty(catalog.Plugins);
    }

    [Fact]
    public void Build_ReportsMissingPluginDependency()
    {
        var directory = CreatePluginDirectory("missing-dependency");
        File.WriteAllText(Path.Combine(directory, "plugin.json"), """
        { "Id": "sample.missing", "Version": "1.0.0", "EntryAssembly": "sample.dll", "Dependencies": ["not.installed"] }
        """);

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddWhiteBoardFramework().AddStartupPluginsFromDirectory(_root);

        using var host = builder.BuildWhiteBoardFrameworkHost();
        var failure = Assert.Single(host.Services.GetRequiredService<IPluginCatalog>().Failures);

        Assert.Equal("sample.missing", failure.Id);
        Assert.Equal(PluginLoadFailureKind.MissingDependency, failure.Kind);
    }

    [Fact]
    public void Build_ReportsDuplicatePluginIdsCaseInsensitively()
    {
        WriteManifest(CreatePluginDirectory("first"), "sample.plugin");
        WriteManifest(CreatePluginDirectory("second"), "SAMPLE.PLUGIN");

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddWhiteBoardFramework().AddStartupPluginsFromDirectory(_root);

        using var host = builder.BuildWhiteBoardFrameworkHost();
        var failures = host.Services.GetRequiredService<IPluginCatalog>().Failures;

        Assert.Equal(2, failures.Count);
        Assert.All(failures, failure => Assert.Equal(PluginLoadFailureKind.DuplicatePluginId, failure.Kind));
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
