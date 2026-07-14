namespace WhiteBoard.Framework.Hosting.Tests;

public sealed class FrameworkLifecycleTests
{
    [Fact]
    public async Task StartAndStop_RunsModulesStartupTasksAndLifetimeAwareServicesInDefinedOrder()
    {
        var events = new List<string>();
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddWhiteBoardFramework()
            .AddModule(new FirstModule(events))
            .AddModule(new SecondModule(events));

        using var host = builder.BuildWhiteBoardFrameworkHost();
        await host.StartAsync();
        await host.StopAsync();

        Assert.Equal(
        [
            "state:Starting",
            "initialize:first",
            "initialize:second",
            "task:first",
            "task:second",
            "start:first",
            "start:second",
            "state:Started",
            "state:Stopping",
            "stop:second",
            "stop:first",
            "state:Stopped"
        ], events);
    }

    [Fact]
    public async Task Start_FailureStopsInitializedModulesInReverseOrder()
    {
        var events = new List<string>();
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddWhiteBoardFramework()
            .AddModule(new FirstModule(events))
            .AddModule(new FailingStartModule(events));

        using var host = builder.BuildWhiteBoardFrameworkHost();

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

        Assert.Equal(
        [
            "state:Starting",
            "initialize:first",
            "initialize:failing",
            "task:first",
            "start:first",
            "start:failing",
            "stop:failing",
            "stop:first"
        ], events);
    }

    private sealed class FirstModule(List<string> events) : TestModule(events, "first")
    {
        public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton<IStartupTask>(new RecordingStartupTask(Events, "first"));
            services.AddSingleton<IApplicationLifetimeAware>(new RecordingLifetimeAware(Events));
        }
    }

    private sealed class SecondModule(List<string> events) : TestModule(events, "second"), IModuleDescriptorProvider
    {
        public ModuleDescriptor GetDescriptor()
            => new(nameof(SecondModule), [typeof(FirstModule)]);

        public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
            => services.AddSingleton<IStartupTask>(new RecordingStartupTask(Events, "second"));
    }

    private sealed class FailingStartModule(List<string> events) : TestModule(events, "failing"), IModuleDescriptorProvider
    {
        public ModuleDescriptor GetDescriptor()
            => new(nameof(FailingStartModule), [typeof(FirstModule)]);

        public override Task StartAsync(ModuleContext context, CancellationToken cancellationToken)
        {
            Events.Add("start:failing");
            throw new InvalidOperationException("Expected startup failure.");
        }
    }

    private abstract class TestModule(List<string> events, string name) : IModule, IAsyncModuleLifecycle
    {
        protected List<string> Events { get; } = events;

        public virtual void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }

        public Task InitializeAsync(ModuleContext context, CancellationToken cancellationToken)
        {
            Events.Add($"initialize:{name}");
            return Task.CompletedTask;
        }

        public virtual Task StartAsync(ModuleContext context, CancellationToken cancellationToken)
        {
            Events.Add($"start:{name}");
            return Task.CompletedTask;
        }

        public Task StopAsync(ModuleContext context, CancellationToken cancellationToken)
        {
            Events.Add($"stop:{name}");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingStartupTask(List<string> events, string name) : IStartupTask
    {
        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            events.Add($"task:{name}");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLifetimeAware(List<string> events) : IApplicationLifetimeAware
    {
        public void OnStateChanged(ApplicationState state) => events.Add($"state:{state}");
    }
}
