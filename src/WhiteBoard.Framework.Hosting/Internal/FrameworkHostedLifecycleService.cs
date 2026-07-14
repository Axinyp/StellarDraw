using Microsoft.Extensions.Hosting;
using WhiteBoard.Framework.Hosting.Lifecycle;
using WhiteBoard.Framework.Hosting.Modules;
using WhiteBoard.Framework.Hosting.Startup;

namespace WhiteBoard.Framework.Hosting.Internal;

/// <summary>
/// 接入 Generic Host 的框架生命周期协调器。
/// 按模块依赖顺序初始化和启动，并在停止或启动失败时按相反顺序释放已初始化模块。
/// </summary>
internal sealed class FrameworkHostedLifecycleService(
    ApplicationLifetime lifetime,
    IModuleCatalog moduleCatalog,
    IConfiguration configuration,
    IServiceProvider services) : IHostedLifecycleService
{
    private readonly List<IAsyncModuleLifecycle> _initialized = [];
    private readonly List<IAsyncModuleLifecycle> _started = [];
    private bool _lifetimeAwareAttached;

    public Task StartingAsync(CancellationToken cancellationToken)
        => StartFrameworkAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        lifetime.MoveTo(ApplicationState.Started);
        return Task.CompletedTask;
    }

    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        lifetime.MoveTo(ApplicationState.Stopping);
        await StopModulesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        lifetime.MoveTo(ApplicationState.Stopped);
        return Task.CompletedTask;
    }

    private async Task StartFrameworkAsync(CancellationToken cancellationToken)
    {
        AttachLifetimeAwareServices();
        lifetime.MoveTo(ApplicationState.Starting);

        try
        {
            foreach (var registration in moduleCatalog.Modules)
            {
                if (registration.Module is not IAsyncModuleLifecycle lifecycle) continue;
                await lifecycle.InitializeAsync(CreateContext(registration), cancellationToken)
                    .ConfigureAwait(false);
                _initialized.Add(lifecycle);
            }

            foreach (var task in services.GetServices<IStartupTask>())
                await task.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            foreach (var registration in moduleCatalog.Modules)
            {
                if (registration.Module is not IAsyncModuleLifecycle lifecycle) continue;
                await lifecycle.StartAsync(CreateContext(registration), cancellationToken)
                    .ConfigureAwait(false);
                _started.Add(lifecycle);
            }
        }
        catch
        {
            await StopInitializedModulesAfterFailedStartAsync().ConfigureAwait(false);
            throw;
        }
    }

    private ModuleContext CreateContext(ModuleRegistration registration)
        => new(registration, configuration, services);

    private void AttachLifetimeAwareServices()
    {
        if (_lifetimeAwareAttached) return;

        foreach (var aware in services.GetServices<IApplicationLifetimeAware>())
        {
            var component = aware;
            lifetime.StateChanged += (_, state) => component.OnStateChanged(state);
        }
        _lifetimeAwareAttached = true;
    }

    private async Task StopInitializedModulesAfterFailedStartAsync()
    {
        foreach (var lifecycle in _initialized.AsEnumerable().Reverse())
        {
            try { await lifecycle.StopAsync(CreateContextFor(lifecycle), CancellationToken.None).ConfigureAwait(false); }
            catch { /* 继续释放其他已初始化模块，保留原始启动异常。 */ }
        }
        _initialized.Clear();
        _started.Clear();
    }

    private async Task StopModulesAsync(CancellationToken cancellationToken)
    {
        List<Exception>? failures = null;
        foreach (var lifecycle in _started.AsEnumerable().Reverse())
        {
            try { await lifecycle.StopAsync(CreateContextFor(lifecycle), cancellationToken).ConfigureAwait(false); }
            catch (Exception ex)
            {
                failures ??= [];
                failures.Add(ex);
            }
        }
        _started.Clear();
        _initialized.Clear();

        if (failures is not null)
            throw new AggregateException("一个或多个模块停止失败。", failures);
    }

    private ModuleContext CreateContextFor(IAsyncModuleLifecycle lifecycle)
    {
        var registration = moduleCatalog.Modules.Single(r => ReferenceEquals(r.Module, lifecycle));
        return CreateContext(registration);
    }
}
