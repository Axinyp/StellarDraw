using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace WhiteBoard.Framework.Hosting.Wpf.Internal;

/// <summary>由框架创建的 View/VM/scope 组合。释放时先断开 DataContext，再释放 scope。</summary>
internal sealed class ViewHandle(IServiceScope scope, object viewModel, FrameworkElement view) : IDisposable
{
    private IServiceScope? _scope = scope;

    public object ViewModel { get; } = viewModel;

    public FrameworkElement View { get; } = view;

    public Type ViewModelType => ViewModel.GetType();

    public void Dispose()
    {
        if (ReferenceEquals(View.DataContext, ViewModel))
            View.DataContext = null;
        Interlocked.Exchange(ref _scope, null)?.Dispose();
    }
}
