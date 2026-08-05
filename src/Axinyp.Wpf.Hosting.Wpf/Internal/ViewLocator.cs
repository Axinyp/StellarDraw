namespace Axinyp.Wpf.Hosting.Wpf.Internal;

/// <summary>基于显式注册表的视图定位器。</summary>
internal sealed class ViewLocator(IEnumerable<ViewRegistration> registrations) : IViewLocator
{
    private readonly IReadOnlyDictionary<Type, Type> _views = CreateMap(registrations);

    public Type GetViewType(Type viewModelType)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);
        return _views.TryGetValue(viewModelType, out var viewType)
            ? viewType
            : throw new InvalidOperationException(
                $"ViewModel '{viewModelType.FullName}' 未注册 View 映射。请调用 AddView<{viewModelType.Name}, TView>()。 ");
    }

    private static IReadOnlyDictionary<Type, Type> CreateMap(IEnumerable<ViewRegistration> registrations)
    {
        var map = new Dictionary<Type, Type>();
        foreach (var registration in registrations)
        {
            if (!map.TryAdd(registration.ViewModelType, registration.ViewType))
                throw new InvalidOperationException(
                    $"ViewModel '{registration.ViewModelType.FullName}' 注册了多个 View 映射。");
        }
        return map;
    }
}
