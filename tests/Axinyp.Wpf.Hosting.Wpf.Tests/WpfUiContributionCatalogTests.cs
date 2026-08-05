using System.Windows.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Axinyp.Wpf.Hosting.Extensions;
using Axinyp.Wpf.Hosting.Abstractions.Modules;
using Axinyp.Wpf.Hosting.Abstractions.Wpf.Plugins;

namespace Axinyp.Wpf.Hosting.Wpf.Tests;

public sealed class WpfUiContributionCatalogTests
{
    [Fact]
    public void Catalog_OrdersByModuleDependenciesAndIsolatesBrokenProvider()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddWpfPresentation();
        builder.AddModularHosting()
            .AddModule(new DependentModule())
            .AddModule(new BrokenModule())
            .AddModule(new FoundationModule());

        using var host = builder.BuildModularHost();
        var catalog = host.Services.GetRequiredService<IWpfUiContributionCatalog>();

        Assert.Collection(catalog.NavigationPages,
            page => Assert.Equal("foundation.page", page.Id),
            page => Assert.Equal("dependent.page", page.Id));
        Assert.Collection(catalog.Commands,
            command => Assert.Equal("foundation.command", command.Id),
            command => Assert.Equal("dependent.command", command.Id));
        Assert.Single(catalog.Failures);
        Assert.Equal(WpfUiContributionFailureKind.ProviderExecutionFailed, catalog.Failures[0].Kind);
        Assert.Equal("UI 贡献 Provider 执行失败。", catalog.Failures[0].Message);
        Assert.Equal(WpfHostingEventIds.UiContributionProviderExecutionFailed.Id, catalog.Failures[0].EventId);
        Assert.Equal(typeof(InvalidOperationException).FullName, catalog.Failures[0].ExceptionType);
    }

    [Fact]
    public void Catalog_LogsProviderFailureAndDisposesProviderScope()
    {
        var probe = new DisposalProbe();
        var loggerProvider = new RecordingLoggerProvider();
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(loggerProvider);
        builder.Services.AddWpfPresentation();
        builder.AddModularHosting().AddModule(new ScopedBrokenModule(probe));

        using var host = builder.BuildModularHost();
        _ = host.Services.GetRequiredService<IWpfUiContributionCatalog>().Failures;

        Assert.True(probe.Disposed);
        var failure = Assert.Single(loggerProvider.Entries, entry =>
            entry.EventId == WpfHostingEventIds.UiContributionProviderExecutionFailed);
        Assert.Equal(LogLevel.Warning, failure.LogLevel);
        Assert.IsType<InvalidOperationException>(failure.Exception);
        Assert.Equal(typeof(ScopedBrokenModule).FullName, failure.Properties["SourceModuleId"]);
        Assert.Equal(typeof(ScopedBrokenProvider).FullName, failure.Properties["ProviderType"]);
        Assert.Equal(WpfUiContributionFailureKind.ProviderExecutionFailed, failure.Properties["FailureKind"]);
    }

    [Fact]
    public void FailureModel_DoesNotExposeExceptionInstance()
    {
        Assert.DoesNotContain(typeof(WpfUiContributionFailure).GetProperties(), property =>
            typeof(Exception).IsAssignableFrom(property.PropertyType));
    }

    private sealed class FoundationModule : IModule
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddTransient<FoundationCommand>();
            services.AddWpfUiContributions<FoundationModule, FoundationProvider>();
        }
    }

    private sealed class DependentModule : IModule, IModuleDescriptorProvider
    {
        public ModuleDescriptor GetDescriptor() => new(nameof(DependentModule), [typeof(FoundationModule)]);

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddTransient<DependentCommand>();
            services.AddWpfUiContributions<DependentModule, DependentProvider>();
        }
    }

    private sealed class BrokenModule : IModule
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
            => services.AddWpfUiContributions<BrokenModule, BrokenProvider>();
    }

    private sealed class ScopedBrokenModule(DisposalProbe probe) : IModule
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped(_ => probe);
            services.AddScoped<ScopedBrokenProvider>();
            services.AddWpfUiContributions<ScopedBrokenModule, ScopedBrokenProvider>();
        }
    }

    private sealed class FoundationProvider : IWpfUiContributionProvider
    {
        public WpfUiContributions GetContributions() => new()
        {
            NavigationPages = [new("foundation.page", "Foundation", "Main", typeof(FoundationViewModel), 10)],
            Commands = [new("foundation.command", "Foundation", typeof(FoundationCommand), WpfCommandPlacement.Menu, Order: 10)]
        };
    }

    private sealed class DependentProvider : IWpfUiContributionProvider
    {
        public WpfUiContributions GetContributions() => new()
        {
            NavigationPages = [new("dependent.page", "Dependent", "Main", typeof(DependentViewModel), 20)],
            Commands = [new("dependent.command", "Dependent", typeof(DependentCommand), WpfCommandPlacement.Toolbar, Order: 20)]
        };
    }

    private sealed class BrokenProvider : IWpfUiContributionProvider
    {
        public WpfUiContributions GetContributions() => throw new InvalidOperationException("Expected provider failure.");
    }

    private sealed class ScopedBrokenProvider : IWpfUiContributionProvider
    {
        public ScopedBrokenProvider(DisposalProbe probe) => _ = probe;

        public WpfUiContributions GetContributions() => throw new InvalidOperationException("Expected scoped provider failure.");
    }

    private sealed class DisposalProbe : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class FoundationViewModel;
    private sealed class DependentViewModel;

    private sealed class FoundationCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
    }

    private sealed class DependentCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
    }
}
