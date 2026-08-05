using Microsoft.Extensions.Hosting;
using Axinyp.Wpf.Hosting.Abstractions.Lifecycle;
using Axinyp.Wpf.Hosting.Abstractions.Modules;
using Axinyp.Wpf.Hosting.Abstractions.Startup;

namespace Axinyp.Wpf.Hosting.Internal;

/// <summary>
/// 接入 Generic Host 的框架生命周期协调器。
/// 按模块依赖顺序初始化和启动，并在停止或启动失败时按相反顺序释放已初始化模块。
/// </summary>
internal sealed class FrameworkHostedLifecycleService(
    ApplicationLifetime lifetime,
    IModuleCatalog moduleCatalog,
    IConfiguration configuration,
    IServiceProvider services,
    ILogger<FrameworkHostedLifecycleService> logger) : IHostedLifecycleService
{
    private static readonly EventId FrameworkStarting = new(1000, nameof(FrameworkStarting));
    private static readonly EventId FrameworkStarted = new(1001, nameof(FrameworkStarted));
    private static readonly EventId FrameworkStopping = new(1002, nameof(FrameworkStopping));
    private static readonly EventId FrameworkStopped = new(1003, nameof(FrameworkStopped));
    private static readonly EventId ModuleInitializing = new(1010, nameof(ModuleInitializing));
    private static readonly EventId ModuleStarting = new(1011, nameof(ModuleStarting));
    private static readonly EventId ModuleStopping = new(1012, nameof(ModuleStopping));
    private static readonly EventId StartupTaskExecuting = new(1020, nameof(StartupTaskExecuting));
    private static readonly EventId StartupFailed = new(1030, nameof(StartupFailed));
    private static readonly EventId ModuleRollbackFailed = new(1031, nameof(ModuleRollbackFailed));
    private static readonly EventId ModuleStopFailed = new(1032, nameof(ModuleStopFailed));

    private readonly List<IAsyncModuleLifecycle> _initialized = [];
    private readonly List<IAsyncModuleLifecycle> _started = [];
    private bool _lifetimeAwareAttached;

    public Task StartingAsync(CancellationToken cancellationToken)
        => StartFrameworkAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        lifetime.MoveTo(ApplicationState.Started);
        logger.LogInformation(FrameworkStarted, "模块化宿主已进入 {ApplicationState} 状态。", ApplicationState.Started);
        return Task.CompletedTask;
    }

    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        lifetime.MoveTo(ApplicationState.Stopping);
        logger.LogInformation(FrameworkStopping, "模块化宿主正在进入 {ApplicationState} 状态。", ApplicationState.Stopping);
        await StopModulesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        lifetime.MoveTo(ApplicationState.Stopped);
        logger.LogInformation(FrameworkStopped, "模块化宿主已进入 {ApplicationState} 状态。", ApplicationState.Stopped);
        return Task.CompletedTask;
    }

    private async Task StartFrameworkAsync(CancellationToken cancellationToken)
    {
        AttachLifetimeAwareServices();
        lifetime.MoveTo(ApplicationState.Starting);
        logger.LogInformation(FrameworkStarting, "模块化宿主正在进入 {ApplicationState} 状态。", ApplicationState.Starting);

        try
        {
            foreach (var registration in moduleCatalog.Modules)
            {
                if (registration.Module is not IAsyncModuleLifecycle lifecycle) continue;
                logger.LogInformation(ModuleInitializing, "正在初始化模块 {ModuleType}。", GetModuleType(registration));
                await lifecycle.InitializeAsync(CreateContext(registration), cancellationToken)
                    .ConfigureAwait(false);
                _initialized.Add(lifecycle);
            }

            foreach (var task in services.GetServices<IStartupTask>())
            {
                logger.LogInformation(StartupTaskExecuting, "正在执行启动任务 {StartupTaskType}。", task.GetType().FullName);
                await task.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var registration in moduleCatalog.Modules)
            {
                if (registration.Module is not IAsyncModuleLifecycle lifecycle) continue;
                logger.LogInformation(ModuleStarting, "正在启动模块 {ModuleType}。", GetModuleType(registration));
                await lifecycle.StartAsync(CreateContext(registration), cancellationToken)
                    .ConfigureAwait(false);
                _started.Add(lifecycle);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(StartupFailed, ex, "模块化宿主启动失败，正在回滚已初始化模块。");
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
            var registration = GetRegistration(lifecycle);
            try
            {
                logger.LogInformation(ModuleStopping, "正在回滚模块 {ModuleType}。", GetModuleType(registration));
                await lifecycle.StopAsync(CreateContext(registration), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ModuleRollbackFailed, ex, "回滚模块 {ModuleType} 时失败。", GetModuleType(registration));
            }
        }
        _initialized.Clear();
        _started.Clear();
    }

    private async Task StopModulesAsync(CancellationToken cancellationToken)
    {
        List<Exception>? failures = null;
        foreach (var lifecycle in _started.AsEnumerable().Reverse())
        {
            var registration = GetRegistration(lifecycle);
            try
            {
                logger.LogInformation(ModuleStopping, "正在停止模块 {ModuleType}。", GetModuleType(registration));
                await lifecycle.StopAsync(CreateContext(registration), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ModuleStopFailed, ex, "停止模块 {ModuleType} 时失败。", GetModuleType(registration));
                failures ??= [];
                failures.Add(ex);
            }
        }
        _started.Clear();
        _initialized.Clear();

        if (failures is not null)
            throw new AggregateException("一个或多个模块停止失败。", failures);
    }

    private ModuleRegistration GetRegistration(IAsyncModuleLifecycle lifecycle)
        => moduleCatalog.Modules.Single(registration => ReferenceEquals(registration.Module, lifecycle));

    private static string GetModuleType(ModuleRegistration registration)
        => registration.Module.GetType().FullName ?? registration.Module.GetType().Name;
}
