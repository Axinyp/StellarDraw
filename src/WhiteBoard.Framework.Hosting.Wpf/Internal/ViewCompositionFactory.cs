using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace WhiteBoard.Framework.Hosting.Wpf.Internal;

/// <summary>统一创建 View/VM 组合，确保区域和对话框共享一致的 DI scope 与 DataContext 生命周期。</summary>
internal sealed class ViewCompositionFactory(IViewLocator viewLocator, IServiceScopeFactory scopeFactory)
{
    public ViewHandle Create(Type viewModelType)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);
        var scope = scopeFactory.CreateScope();
        try
        {
            var viewModel = scope.ServiceProvider.GetRequiredService(viewModelType);
            var viewType = viewLocator.GetViewType(viewModelType);
            if (!typeof(FrameworkElement).IsAssignableFrom(viewType))
                throw new InvalidOperationException($"View '{viewType.FullName}' 必须继承 FrameworkElement。");

            var view = (FrameworkElement)scope.ServiceProvider.GetRequiredService(viewType);
            view.DataContext = viewModel;
            return new ViewHandle(scope, viewModel, view);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
