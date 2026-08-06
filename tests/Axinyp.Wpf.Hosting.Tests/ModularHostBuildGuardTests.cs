namespace Axinyp.Wpf.Hosting.Tests;

/// <summary>BuildModularHost 装配边界的失败路径与误用检测。</summary>
public sealed class ModularHostBuildGuardTests
{
    [Fact]
    public void BuildModularHost_AfterModuleConfigureServicesFailure_RefusesRetryWithClearGuidance()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddModularHosting()
            .AddModule(new HealthyModule())
            .AddModule(new ThrowingConfigureModule());

        var first = Assert.Throws<InvalidOperationException>(() => builder.BuildModularHost());
        Assert.Equal("Expected configure failure.", first.Message);

        // ConfigureServices 中途失败后服务集合已被部分写入且无法回滚：
        // 重试必须被明确拒绝，而不是把前面模块的服务重复注册一遍。
        var second = Assert.Throws<InvalidOperationException>(() => builder.BuildModularHost());
        Assert.Contains("重建 HostApplicationBuilder", second.Message);
    }

    [Fact]
    public async Task Start_WhenBuilderBuildBypassesBuildModularHost_FailsFastWithGuidance()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddModularHosting().AddModule(new HealthyModule());

        // 误用：绕过 BuildModularHost 直接 Build——模块的 ConfigureServices 从未执行。
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
        Assert.Contains("BuildModularHost", exception.Message);
    }

    [Fact]
    public async Task Start_WhenNoModulesWereStaged_PlainBuildStillWorks()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddModularHosting();

        using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();
    }

    private sealed class HealthyModule : IModule
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
            => services.AddSingleton(new object());
    }

    private sealed class ThrowingConfigureModule : IModule
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
            => throw new InvalidOperationException("Expected configure failure.");
    }
}
