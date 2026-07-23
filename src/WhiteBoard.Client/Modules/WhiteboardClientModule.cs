using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WhiteBoard.Client.Storage;
using WhiteBoard.Client.ViewModels;
using WhiteBoard.Contracts.Services;
using WhiteBoard.Core.Domain;
using WhiteBoard.Core.InkForecast;
using WhiteBoard.Framework.Hosting.Modules;
using WhiteBoard.UI.ViewModels;

namespace WhiteBoard.Client.Modules;

/// <summary>
/// Client 的离线白板业务模块。它复用 Host 的模块生命周期，但不依赖旧 Demo 的运行时插件模型。
/// </summary>
public sealed class WhiteboardClientModule : IModule, IAsyncModuleLifecycle, IModuleDescriptorProvider
{
    public ModuleDescriptor GetDescriptor() => new("whiteboard-client");

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ICollaborationAdapter>(NullCollaborationAdapter.Instance);
        services.AddSingleton<WhiteboardSession>();
        services.AddSingleton<ILocalWhiteboardDocumentStore, LocalWhiteboardDocumentStore>();
        services.AddSingleton<WhiteboardViewModel>(serviceProvider =>
        {
            var dispatcher = Application.Current?.Dispatcher
                ?? throw new InvalidOperationException("WPF Application Dispatcher 不可用。");
            return new WhiteboardViewModel(
                serviceProvider.GetRequiredService<WhiteboardSession>(),
                dispatcher);
        });
        services.AddSingleton<MainWindowViewModel>();
    }

    public Task InitializeAsync(ModuleContext context, CancellationToken cancellationToken)
    {
        var session = context.Services.GetRequiredService<WhiteboardSession>();
        session.RegisterPredictor(new KalmanPredictor());
        return Task.CompletedTask;
    }

    public Task StartAsync(ModuleContext context, CancellationToken cancellationToken) =>
        context.Services.GetRequiredService<WhiteboardSession>().StartAsync(cancellationToken);

    public Task StopAsync(ModuleContext context, CancellationToken cancellationToken) =>
        context.Services.GetRequiredService<WhiteboardSession>().StopAsync();
}
