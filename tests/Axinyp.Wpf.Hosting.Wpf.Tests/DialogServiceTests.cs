using System.Collections.Concurrent;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Axinyp.Wpf.Hosting.Abstractions.Wpf;
using Axinyp.Wpf.Hosting.Wpf.Extensions;

namespace Axinyp.Wpf.Hosting.Wpf.Tests;

public sealed class DialogServiceTests
{
    [Fact]
    public async Task ShowDialogAsync_ProcessesDeferredCloseRunsLifecycleAndDisposesScope()
    {
        await Sta.RunAsync(async () =>
        {
            var probe = new DisposalProbe();
            var services = new ServiceCollection();
            services.AddWpfPresentation();
            services.AddScoped(_ => probe);
            services.AddScoped<DeferredCloseDialogViewModel>();
            services.AddView<DeferredCloseDialogViewModel, DeferredCloseDialog>();
            using var provider = services.BuildServiceProvider();
            var dialogs = provider.GetRequiredService<IDialogService>();
            var owner = CreateVisibleOwner();
            var parameters = new DialogParameters { ["DocumentId"] = "lesson-42" };

            try
            {
                var result = await dialogs.ShowDialogAsync<DeferredCloseDialogViewModel>(owner, parameters);
                var viewModel = Assert.IsType<DeferredCloseDialogViewModel>(probe.ViewModel);

                Assert.True(result.Result);
                Assert.Equal("saved", result.Payload);
                Assert.Same(parameters, viewModel.OpenedParameters);
                Assert.Equal(1, viewModel.OpenedCount);
                Assert.Equal(1, viewModel.ClosedCount);
                Assert.True(viewModel.CanCloseCount >= 1);
                Assert.Equal(0, viewModel.RequestCloseSubscriberCount);
                Assert.True(probe.Disposed);
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public async Task ShowDialogAsync_CanCloseRejectsRequestUntilViewModelAllowsClosing()
    {
        await Sta.RunAsync(async () =>
        {
            var services = new ServiceCollection();
            services.AddWpfPresentation();
            services.AddScoped<GuardedDialogViewModel>();
            services.AddView<GuardedDialogViewModel, GuardedDialog>();
            using var provider = services.BuildServiceProvider();
            var dialogs = provider.GetRequiredService<IDialogService>();
            var owner = CreateVisibleOwner();

            try
            {
                var result = await dialogs.ShowDialogAsync<GuardedDialogViewModel>(owner);
                var viewModel = Assert.IsType<GuardedDialogViewModel>(GuardedDialogViewModel.LastCreated);

                Assert.True(result.Result);
                Assert.True(viewModel.CanCloseCount >= 2);
                Assert.Equal(1, viewModel.ClosedCount);
                Assert.Equal(0, viewModel.RequestCloseSubscriberCount);
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public async Task ShowDialogAsync_WhenViewModelRequestsCloseFromBackgroundThread_ClosesOnOwnerDispatcher()
    {
        await Sta.RunAsync(async () =>
        {
            var probe = new DisposalProbe();
            var services = new ServiceCollection();
            services.AddWpfPresentation();
            services.AddScoped(_ => probe);
            services.AddScoped<BackgroundCloseDialogViewModel>();
            services.AddView<BackgroundCloseDialogViewModel, BackgroundCloseDialog>();
            using var provider = services.BuildServiceProvider();
            var dialogs = provider.GetRequiredService<IDialogService>();
            var owner = CreateVisibleOwner();

            try
            {
                var result = await dialogs.ShowDialogAsync<BackgroundCloseDialogViewModel>(owner);
                var viewModel = Assert.IsType<BackgroundCloseDialogViewModel>(probe.ViewModel);

                Assert.True(result.Result);
                Assert.True(viewModel.RequestRaisedOnBackground);
                Assert.NotEmpty(viewModel.CanCloseDispatcherAccesses);
                Assert.All(viewModel.CanCloseDispatcherAccesses, Assert.True);
                Assert.True(viewModel.ClosedOnOwnerDispatcher);
                Assert.Equal(0, viewModel.RequestCloseSubscriberCount);
                Assert.True(probe.Disposed);
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public async Task ShowDialogAsync_WhenOwnerDispatcherIsUnavailable_DoesNotCreateScopeOrDialog()
    {
        await Sta.RunAsync(async () =>
        {
            NeverCreatedDialogViewModel.Reset();
            var services = new ServiceCollection();
            services.AddSingleton<IUiDispatcher>(new RejectingUiDispatcher(Application.Current.Dispatcher));
            services.AddWpfPresentation();
            services.AddTransient<NeverCreatedDialogViewModel>();
            services.AddView<NeverCreatedDialogViewModel, NeverCreatedDialog>();
            using var provider = services.BuildServiceProvider();
            var dialogs = provider.GetRequiredService<IDialogService>();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => dialogs.ShowDialogAsync<NeverCreatedDialogViewModel>(new Window()));

            Assert.Equal(0, NeverCreatedDialogViewModel.CreatedCount);
        });
    }

    [Fact]
    public async Task ShowDialogAsync_PreCancelledTokenDoesNotCreateDialog()
    {
        await Sta.RunAsync(async () =>
        {
            var services = new ServiceCollection();
            services.AddWpfPresentation();
            services.AddTransient<NeverCreatedDialogViewModel>();
            services.AddView<NeverCreatedDialogViewModel, NeverCreatedDialog>();
            using var provider = services.BuildServiceProvider();
            var dialogs = provider.GetRequiredService<IDialogService>();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => dialogs.ShowDialogAsync<NeverCreatedDialogViewModel>(new Window(), cancellationToken: cancellation.Token));

            Assert.Equal(0, NeverCreatedDialogViewModel.CreatedCount);
        });
    }

    [Fact]
    public async Task ShowDialogAsync_WhenCanCloseDialogThrowsDuringWindowClosing_ClosesWithCancelInsteadOfCrashing()
    {
        await Sta.RunAsync(async () =>
        {
            var services = new ServiceCollection();
            services.AddWpfPresentation();
            services.AddScoped<ThrowingGuardDialogViewModel>();
            services.AddView<ThrowingGuardDialogViewModel, SelfClosingDialog>();
            using var provider = services.BuildServiceProvider();
            var dialogs = provider.GetRequiredService<IDialogService>();
            var owner = CreateVisibleOwner();

            try
            {
                var result = await dialogs.ShowDialogAsync<ThrowingGuardDialogViewModel>(owner)
                    .WaitAsync(TimeSpan.FromSeconds(10));
                var viewModel = Assert.IsType<ThrowingGuardDialogViewModel>(ThrowingGuardDialogViewModel.LastCreated);

                // 守卫抛异常时按取消结果放行关闭，而不是让异常沿 Closing 冒泡成进程级未处理异常。
                Assert.NotEqual(true, result.Result);
                Assert.Equal(1, viewModel.ClosedCount);
                Assert.True(viewModel.GuardThrown);
            }
            finally
            {
                owner.Close();
            }
        });
    }

    private static Window CreateVisibleOwner()
    {
        var owner = new Window
        {
            Width = 100,
            Height = 100,
            ShowInTaskbar = false
        };
        owner.Show();
        return owner;
    }

    private sealed class DeferredCloseDialogViewModel(DisposalProbe probe) : IDialogAware, IDisposable
    {
        private EventHandler<IDialogResult>? _requestClose;

        public event EventHandler<IDialogResult>? RequestClose
        {
            add
            {
                _requestClose += value;
                RequestCloseSubscriberCount++;
            }
            remove
            {
                _requestClose -= value;
                RequestCloseSubscriberCount--;
            }
        }

        public int RequestCloseSubscriberCount { get; private set; }
        public int OpenedCount { get; private set; }
        public int ClosedCount { get; private set; }
        public int CanCloseCount { get; private set; }
        public DialogParameters? OpenedParameters { get; private set; }

        public void OnDialogOpened(DialogParameters parameters)
        {
            probe.ViewModel = this;
            OpenedParameters = parameters;
            OpenedCount++;
            _requestClose?.Invoke(this, DialogResult.Ok("saved"));
        }

        public bool CanCloseDialog()
        {
            CanCloseCount++;
            return true;
        }

        public void OnDialogClosed() => ClosedCount++;

        public void Dispose() => probe.Disposed = true;
    }

    private sealed class BackgroundCloseDialogViewModel(DisposalProbe probe) : IDialogAware, IDisposable
    {
        private EventHandler<IDialogResult>? _requestClose;

        public event EventHandler<IDialogResult>? RequestClose
        {
            add
            {
                _requestClose += value;
                RequestCloseSubscriberCount++;
            }
            remove
            {
                _requestClose -= value;
                RequestCloseSubscriberCount--;
            }
        }

        public int RequestCloseSubscriberCount { get; private set; }
        public bool RequestRaisedOnBackground { get; private set; }
        public ConcurrentQueue<bool> CanCloseDispatcherAccesses { get; } = [];
        public bool ClosedOnOwnerDispatcher { get; private set; }

        public void OnDialogOpened(DialogParameters parameters)
        {
            probe.ViewModel = this;
            _ = Task.Run(() =>
            {
                RequestRaisedOnBackground = !Application.Current.Dispatcher.CheckAccess();
                _requestClose?.Invoke(this, DialogResult.Ok());
            });
        }

        public bool CanCloseDialog()
        {
            CanCloseDispatcherAccesses.Enqueue(Application.Current.Dispatcher.CheckAccess());
            return true;
        }

        public void OnDialogClosed() => ClosedOnOwnerDispatcher = Application.Current.Dispatcher.CheckAccess();

        public void Dispose() => probe.Disposed = true;
    }

    private sealed class GuardedDialogViewModel : IDialogAware
    {
        private EventHandler<IDialogResult>? _requestClose;
        private bool _canClose;

        public GuardedDialogViewModel() => LastCreated = this;

        public static GuardedDialogViewModel? LastCreated { get; private set; }

        public event EventHandler<IDialogResult>? RequestClose
        {
            add
            {
                _requestClose += value;
                RequestCloseSubscriberCount++;
            }
            remove
            {
                _requestClose -= value;
                RequestCloseSubscriberCount--;
            }
        }

        public int RequestCloseSubscriberCount { get; private set; }
        public int CanCloseCount { get; private set; }
        public int ClosedCount { get; private set; }

        public void OnDialogOpened(DialogParameters parameters)
        {
            _requestClose?.Invoke(this, DialogResult.Ok());
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                _canClose = true;
                _requestClose?.Invoke(this, DialogResult.Ok());
            });
        }

        public bool CanCloseDialog()
        {
            CanCloseCount++;
            return _canClose;
        }

        public void OnDialogClosed() => ClosedCount++;
    }

    private sealed class NeverCreatedDialogViewModel
    {
        public NeverCreatedDialogViewModel() => CreatedCount++;

        public static int CreatedCount { get; private set; }

        public static void Reset() => CreatedCount = 0;
    }

    private sealed class ThrowingGuardDialogViewModel : IDialogAware
    {
        public ThrowingGuardDialogViewModel() => LastCreated = this;

        public static ThrowingGuardDialogViewModel? LastCreated { get; private set; }

        public event EventHandler<IDialogResult>? RequestClose
        {
            add { }
            remove { }
        }

        public bool GuardThrown { get; private set; }
        public int ClosedCount { get; private set; }

        public void OnDialogOpened(DialogParameters parameters)
        {
        }

        public bool CanCloseDialog()
        {
            GuardThrown = true;
            throw new InvalidOperationException("Expected guard failure.");
        }

        public void OnDialogClosed() => ClosedCount++;
    }

    private sealed class DeferredCloseDialog : Window;
    private sealed class BackgroundCloseDialog : Window;
    private sealed class GuardedDialog : Window;
    private sealed class NeverCreatedDialog : Window;

    private sealed class SelfClosingDialog : Window
    {
        public SelfClosingDialog() => Loaded += (_, _) => Dispatcher.BeginInvoke(Close);
    }

    private sealed class DisposalProbe
    {
        public object? ViewModel { get; set; }
        public bool Disposed { get; set; }
    }
}
