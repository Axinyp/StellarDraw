namespace WhiteBoard.Framework.Hosting.Tests;

public sealed class ModuleOrderingTests
{
    [Fact]
    public void Build_OrdersModulesByDependencies_WhileKeepingUnrelatedRegistrationOrder()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddWhiteBoardFramework()
            .AddModule(new DependentModule())
            .AddModule(new UnrelatedModule())
            .AddModule(new FoundationModule());

        using var host = builder.BuildWhiteBoardFrameworkHost();
        var catalog = host.Services.GetRequiredService<IModuleCatalog>();

        Assert.Collection(catalog.Modules,
            registration => Assert.IsType<UnrelatedModule>(registration.Module),
            registration => Assert.IsType<FoundationModule>(registration.Module),
            registration => Assert.IsType<DependentModule>(registration.Module));
    }

    [Fact]
    public void Build_RejectsDuplicateModuleTypes()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddWhiteBoardFramework()
            .AddModule(new FoundationModule())
            .AddModule(new FoundationModule());

        var exception = Assert.Throws<DuplicateModuleException>(builder.BuildWhiteBoardFrameworkHost);

        Assert.Contains(nameof(FoundationModule), exception.Message);
    }

    [Fact]
    public void Build_RejectsMissingModuleDependency()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddWhiteBoardFramework().AddModule(new DependentModule());

        var exception = Assert.Throws<MissingModuleDependencyException>(builder.BuildWhiteBoardFrameworkHost);

        Assert.Contains(nameof(DependentModule), exception.Message);
        Assert.Contains(nameof(FoundationModule), exception.Message);
    }

    [Fact]
    public void Build_ReportsModuleDependencyCycle()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddWhiteBoardFramework()
            .AddModule(new FirstCycleModule())
            .AddModule(new SecondCycleModule());

        var exception = Assert.Throws<ModuleDependencyCycleException>(builder.BuildWhiteBoardFrameworkHost);

        Assert.Contains(nameof(FirstCycleModule), exception.Message);
        Assert.Contains(nameof(SecondCycleModule), exception.Message);
    }

    private sealed class FoundationModule : TestModule;

    private sealed class DependentModule : TestModule, IModuleDescriptorProvider
    {
        public ModuleDescriptor GetDescriptor()
            => new(nameof(DependentModule), [typeof(FoundationModule)]);
    }

    private sealed class UnrelatedModule : TestModule;

    private sealed class FirstCycleModule : TestModule, IModuleDescriptorProvider
    {
        public ModuleDescriptor GetDescriptor()
            => new(nameof(FirstCycleModule), [typeof(SecondCycleModule)]);
    }

    private sealed class SecondCycleModule : TestModule, IModuleDescriptorProvider
    {
        public ModuleDescriptor GetDescriptor()
            => new(nameof(SecondCycleModule), [typeof(FirstCycleModule)]);
    }

    private abstract class TestModule : IModule
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }
    }
}
