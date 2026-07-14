using WhiteBoard.Framework.Hosting.Lifecycle;

namespace WhiteBoard.Framework.Hosting.Internal;

/// <summary>
/// <see cref="IApplicationLifetime"/> 的默认实现（框架内部，注册为单例）。
/// 线程安全地推进状态并广播 <see cref="StateChanged"/>。状态推进由框架的
/// StartWhiteBoardFramework / StopWhiteBoardFramework 扩展驱动，业务代码不直接调用 <see cref="MoveTo"/>。
/// </summary>
internal sealed class ApplicationLifetime : IApplicationLifetime
{
    private readonly object _gate = new();

    public ApplicationState State { get; private set; } = ApplicationState.Created;

    public event EventHandler<ApplicationState>? StateChanged;

    /// <summary>推进到指定状态；与当前相同则忽略。变更在锁外广播，避免订阅者回调时持锁。</summary>
    public void MoveTo(ApplicationState state)
    {
        lock (_gate)
        {
            if (State == state) return;
            State = state;
        }
        StateChanged?.Invoke(this, state);
    }
}
