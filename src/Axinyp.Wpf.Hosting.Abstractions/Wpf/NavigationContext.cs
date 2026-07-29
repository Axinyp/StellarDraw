namespace Axinyp.Wpf.Hosting.Abstractions.Wpf;

/// <summary>
/// 一次导航操作的上下文信息。
/// </summary>
/// <param name="RegionName">目标区域名称。</param>
/// <param name="CurrentViewModel">当前激活的 ViewModel。</param>
/// <param name="TargetViewModelType">目标 ViewModel 类型。</param>
/// <param name="Parameter">导航参数。</param>
/// <param name="Kind">导航方向。</param>
public sealed record NavigationContext(string RegionName, object? CurrentViewModel, Type? TargetViewModelType,
    object? Parameter, NavigationKind Kind);

/// <summary>
/// 导航方向。
/// </summary>
public enum NavigationKind
{
    /// <summary>前进到新目标。</summary>
    Forward,

    /// <summary>回退到历史目标。</summary>
    Back
}
