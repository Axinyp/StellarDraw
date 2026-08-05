namespace Axinyp.Wpf.Hosting.Abstractions.Lifecycle;

/// <summary>
/// 生命周期感知组件：任何注册到 DI 的服务实现此接口后，
/// 会在应用状态变更时自动收到回调（由框架在启动时把它们挂接到 <see cref="IApplicationLifetime"/>）。
/// 用于在 Started 时启动后台任务、在 Stopping 时优雅收尾等。
/// </summary>
public interface IApplicationLifetimeAware
{
    /// <summary>应用生命周期状态变更回调。</summary>
    /// <param name="state">变更后的新状态。</param>
    void OnStateChanged(ApplicationState state);
}
