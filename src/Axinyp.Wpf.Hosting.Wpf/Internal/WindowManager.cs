using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Axinyp.Wpf.Hosting.Wpf.Internal;

/// <summary>窗口管理器：统一执行 UI 线程调度、DataContext 绑定、Owner 校验和窗口 scope 清理。</summary>
internal sealed class WindowManager(
    IViewLocator viewLocator,
    IServiceScopeFactory scopeFactory,
    IUiDispatcher uiDispatcher) : IWindowManager
{
    public Task<Window> ShowAsync<TViewModel>(Window? owner = null) where TViewModel : class
        => RunOnUiAsync(() => ShowFromScope(typeof(TViewModel), owner));

    public Task<Window> ShowAsync(object viewModel, Window? owner = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        return RunOnUiAsync(() => ShowExisting(viewModel, owner));
    }

    public Task<bool?> ShowDialogAsync<TViewModel>(Window owner) where TViewModel : class
        => RunOnUiAsync(() => ShowDialogFromScope(typeof(TViewModel), owner));

    public Task<bool?> ShowDialogAsync(object viewModel, Window owner)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(owner);
        return RunOnUiAsync(() => ShowExistingDialog(viewModel, owner));
    }

    private Window ShowFromScope(Type viewModelType, Window? owner)
    {
        var scope = scopeFactory.CreateScope();
        try
        {
            var viewModel = scope.ServiceProvider.GetRequiredService(viewModelType);
            var window = CreateWindow(scope.ServiceProvider, viewModel);
            SetOwner(window, owner);
            window.Closed += (_, _) => scope.Dispose();
            window.Show();
            return window;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private Window ShowExisting(object viewModel, Window? owner)
    {
        var scope = scopeFactory.CreateScope();
        try
        {
            var window = CreateWindow(scope.ServiceProvider, viewModel);
            SetOwner(window, owner);
            window.Closed += (_, _) => scope.Dispose();
            window.Show();
            return window;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private bool? ShowDialogFromScope(Type viewModelType, Window owner)
    {
        using var scope = scopeFactory.CreateScope();
        var viewModel = scope.ServiceProvider.GetRequiredService(viewModelType);
        var window = CreateWindow(scope.ServiceProvider, viewModel);
        SetOwner(window, owner);
        return window.ShowDialog();
    }

    private bool? ShowExistingDialog(object viewModel, Window owner)
    {
        using var scope = scopeFactory.CreateScope();
        var window = CreateWindow(scope.ServiceProvider, viewModel);
        SetOwner(window, owner);
        return window.ShowDialog();
    }

    private Window CreateWindow(IServiceProvider services, object viewModel)
    {
        var viewType = viewLocator.GetViewType(viewModel.GetType());
        if (!typeof(Window).IsAssignableFrom(viewType))
            throw new InvalidOperationException(
                $"View '{viewType.FullName}' 不是 Window，不能由 IWindowManager 显示。");

        var window = (Window)ActivatorUtilities.CreateInstance(services, viewType);
        window.DataContext = viewModel;
        return window;
    }

    private static void SetOwner(Window window, Window? owner)
    {
        if (owner is null) return;
        if (!owner.Dispatcher.CheckAccess())
            throw new InvalidOperationException("Owner 必须属于当前 WPF UI Dispatcher。");
        if (!owner.IsVisible || owner.IsClosed())
            throw new InvalidOperationException("Owner 必须是仍处于显示状态的窗口。");
        window.Owner = owner;
    }

    private Task<T> RunOnUiAsync<T>(Func<T> action)
        => uiDispatcher.InvokeAsync(uiDispatcher.GetApplicationDispatcher(), action);
}

internal static class WindowExtensions
{
    public static bool IsClosed(this Window window) => !window.IsLoaded && !window.IsVisible;
}
