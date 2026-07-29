namespace Axinyp.Wpf.Hosting.Abstractions.Lifecycle;

/// <summary>
/// 应用生命周期状态机的各阶段。
/// 由 <see cref="IApplicationLifetime"/> 顺序推进：Created → Starting → Started → Stopping → Stopped。
/// </summary>
public enum ApplicationState
{
    /// <summary>DI 容器已构建，尚未启动。</summary>
    Created,

    /// <summary>正在启动（模块初始化、生命周期感知组件收到通知）。</summary>
    Starting,

    /// <summary>已完成启动，进入正常运行。</summary>
    Started,

    /// <summary>正在停止（准备释放资源）。</summary>
    Stopping,

    /// <summary>已停止。</summary>
    Stopped
}
