namespace Axinyp.Wpf.Hosting.Tests;

public sealed class ServiceLifetimeValidatorTests
{
    [Fact]
    public void ValidateServiceLifetimes_ReportsSingletonCapturingScopedDependency()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedDependency>();
        services.AddSingleton<CapturingSingleton>();

        var issues = services.ValidateServiceLifetimes();

        var issue = Assert.Single(issues);
        Assert.Contains(nameof(CapturingSingleton), issue);
        Assert.Contains(nameof(ScopedDependency), issue);
    }

    [Fact]
    public void ValidateServiceLifetimes_SkipsKeyedRegistrationsInsteadOfThrowing()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IKeyedService, KeyedService>("primary");
        services.AddKeyedScoped<IKeyedService, KeyedService>("secondary");
        services.AddScoped<ScopedDependency>();
        services.AddSingleton<CapturingSingleton>();

        var issues = services.ValidateServiceLifetimes();

        // keyed 注册不参与分析也不得使校验崩溃；普通俘获仍应被报告。
        var issue = Assert.Single(issues);
        Assert.Contains(nameof(CapturingSingleton), issue);
    }

    [Fact]
    public void ValidateServiceLifetimes_ReturnsEmptyForHealthyRegistrations()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ScopedDependency>();
        services.AddSingleton<CapturingSingleton>();

        Assert.Empty(services.ValidateServiceLifetimes());
    }

    private sealed class ScopedDependency;

    private sealed class CapturingSingleton(ScopedDependency dependency)
    {
        public ScopedDependency Dependency { get; } = dependency;
    }

    private interface IKeyedService;

    private sealed class KeyedService : IKeyedService;
}
