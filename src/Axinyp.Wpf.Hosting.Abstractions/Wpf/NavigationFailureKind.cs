namespace Axinyp.Wpf.Hosting.Abstractions.Wpf;

/// <summary>导航未成功完成时的稳定失败分类。</summary>
public enum NavigationFailureKind
{
    /// <summary>没有失败或取消。</summary>
    None,

    /// <summary>导航在执行前或由当前 ViewModel 取消。</summary>
    Cancelled,

    /// <summary>后退请求没有可用的导航记录。</summary>
    NoBackStack,

    /// <summary>目标 Region 的 Dispatcher 已不可用。</summary>
    DispatcherUnavailable,

    /// <summary>ViewModel、View 或其作用域无法组合。</summary>
    ViewComposition,

    /// <summary>目标 ViewModel 的进入生命周期失败。</summary>
    EnteringLifecycle,

    /// <summary>当前 ViewModel 的离开或确认生命周期失败。</summary>
    LeavingLifecycle,

    /// <summary>未能归入其他稳定类别的框架失败。</summary>
    Unexpected,

    /// <summary>在同一区域的导航生命周期回调内又发起并等待了新的导航（会形成自死锁，被立即拒绝）。</summary>
    Reentrant
}
