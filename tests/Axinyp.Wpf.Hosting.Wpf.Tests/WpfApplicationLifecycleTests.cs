using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Axinyp.Wpf.Hosting.Abstractions.Modules;
using Axinyp.Wpf.Hosting.Extensions;

namespace Axinyp.Wpf.Hosting.Wpf.Tests;

public sealed class WpfApplicationLifecycleTests
{
    [Fact]
    public async Task RunAsync_CoordinatedClose_RunsTeardownStopsHostThenClosesWindow()
    {
        await Sta.RunAsync(async () =>
        {
            var events = new List<string>();
            var loggerProvider = new RecordingLoggerProvider();
            var shell = CreateShell();
            var options = new WpfApplicationLifecycleOptions
            {
                HostFactory = () => BuildHost(events, failStop: false, loggerProvider),
                ShellFactory = _ =>
                {
                    shell.Show();
                    return Task.FromResult<Window>(shell);
                },
                BeforeHostStopAsync = () =>
                {
                    events.Add("teardown");
                    return Task.CompletedTask;
                },
                ShowFailureDialogs = false,
                ShutdownAction = _ => events.Add("shutdown"),
            };

            await WpfApplicationLifecycle.RunAsync(Application.Current, options);
            Assert.Contains("start:module", events);
            Assert.True(shell.IsVisible);

            var closed = WaitForClosed(shell);
            shell.Close();
            try
            {
                await closed.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                throw new TimeoutException(
                    $"协调停机未完成。events=[{string.Join(",", events)}]; " +
                    $"logs=[{string.Join(",", loggerProvider.Entries.Select(entry => $"{entry.EventId.Name}:{entry.Exception?.ToString() ?? "-"}"))}]");
            }

            Assert.Equal(
                ["start:module", "teardown", "stop:module"],
                events.Where(item => item is "start:module" or "teardown" or "stop:module"));
            Assert.DoesNotContain("shutdown", events);
        });
    }

    [Fact]
    public async Task RunAsync_WhenHostStopFails_KeepsWindowOpenThenForceClosesOnSecondRequest()
    {
        await Sta.RunAsync(async () =>
        {
            var events = new List<string>();
            var loggerProvider = new RecordingLoggerProvider();
            var shell = CreateShell();
            var options = new WpfApplicationLifecycleOptions
            {
                HostFactory = () => BuildHost(events, failStop: true, loggerProvider),
                ShellFactory = _ =>
                {
                    shell.Show();
                    return Task.FromResult<Window>(shell);
                },
                ShowFailureDialogs = false,
                ShutdownAction = _ => { },
            };

            await WpfApplicationLifecycle.RunAsync(Application.Current, options);

            shell.Close();
            await WaitForLogAsync(loggerProvider, WpfHostingEventIds.ApplicationShutdownFailed);

            // 优雅停机失败：窗口保持打开，等待用户决定。
            Assert.True(shell.IsVisible);

            // 第二次关闭请求强制退出：窗口必须能关闭，容器被释放。
            var closed = WaitForClosed(shell);
            shell.Close();
            await closed.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains("container-disposed", events);
        });
    }

    [Fact]
    public async Task RunAsync_WhenHostFactoryThrows_HandlesFailureAndRequestsShutdown()
    {
        await Sta.RunAsync(async () =>
        {
            var exitCodes = new List<int>();
            var options = new WpfApplicationLifecycleOptions
            {
                HostFactory = () => throw new InvalidOperationException("Expected startup failure."),
                ShellFactory = _ => Task.FromResult<Window>(new Window()),
                ShowFailureDialogs = false,
                ShutdownAction = exitCodes.Add,
            };

            // RunAsync 承诺不抛出：async void OnStartup 调用方依赖该契约。
            await WpfApplicationLifecycle.RunAsync(Application.Current, options);

            Assert.Equal([-1], exitCodes);
        });
    }

    private static Window CreateShell() => new()
    {
        Width = 120,
        Height = 80,
        ShowInTaskbar = false,
        WindowStartupLocation = WindowStartupLocation.Manual,
        Left = -2000,
        Top = -2000,
    };

    private static Task WaitForClosed(Window window)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        return closed.Task;
    }

    private static async Task WaitForLogAsync(RecordingLoggerProvider loggerProvider, EventId eventId)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (loggerProvider.Entries.Any(entry => entry.EventId.Id == eventId.Id))
                return;
            await Task.Delay(25);
        }

        throw new TimeoutException($"未在超时前观察到日志事件 {eventId.Name}。");
    }

    private static IHost BuildHost(
        List<string> events,
        bool failStop = false,
        RecordingLoggerProvider? loggerProvider = null)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        if (loggerProvider is not null)
            builder.Logging.AddProvider(loggerProvider);
        builder.Services.AddSingleton(_ => new ContainerDisposalProbe(events));
        builder.AddModularHosting().AddModule(new RecorderModule(events, failStop));
        var host = builder.BuildModularHost();
        // 立刻实例化探针，让容器 Dispose 时能观察到。
        _ = host.Services.GetRequiredService<ContainerDisposalProbe>();
        return host;
    }

    private sealed class RecorderModule(List<string> events, bool failStop) : IModule, IAsyncModuleLifecycle
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
        }

        public Task InitializeAsync(ModuleContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StartAsync(ModuleContext context, CancellationToken cancellationToken)
        {
            events.Add("start:module");
            return Task.CompletedTask;
        }

        public Task StopAsync(ModuleContext context, CancellationToken cancellationToken)
        {
            if (failStop)
                throw new InvalidOperationException("Expected stop failure.");
            events.Add("stop:module");
            return Task.CompletedTask;
        }
    }

    private sealed class ContainerDisposalProbe(List<string> events) : IDisposable
    {
        public void Dispose() => events.Add("container-disposed");
    }
}
