namespace WhiteBoard.Framework.Hosting.Lifecycle;

/// <summary>
/// 应用生命周期：向外暴露当前状态与状态变更事件。
/// 由框架统一持有（单例），宿主通过 <c>StartWhiteBoardFramework</c> /
/// <c>StopWhiteBoardFramework</c> 扩展方法驱动状态推进，业务组件只读订阅。
/// </summary>
public interface IApplicationLifetime
{
    /// <summary>当前生命周期状态。</summary>
    ApplicationState State { get; }

    /// <summary>状态发生变更时触发（参数为变更后的新状态）。</summary>
    event EventHandler<ApplicationState>? StateChanged;
}
