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
