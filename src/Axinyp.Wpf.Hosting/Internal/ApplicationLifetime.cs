using Axinyp.Wpf.Hosting.Abstractions.Lifecycle;

namespace Axinyp.Wpf.Hosting.Internal;

/// <summary>
/// <see cref="IApplicationLifetime"/> 的默认实现（框架内部，注册为单例）。
/// 线程安全地推进状态并广播 <see cref="StateChanged"/>。状态推进由框架的
/// StartModularHosting / StopModularHosting 扩展驱动，业务代码不直接调用 <see cref="MoveTo"/>。
/// </summary>
internal sealed class ApplicationLifetime(ILogger<ApplicationLifetime> logger) : IApplicationLifetime
{
    private static readonly EventId StateSubscriberFailed = new(1033, nameof(StateSubscriberFailed));

    private readonly object _gate = new();

    public ApplicationState State { get; private set; } = ApplicationState.Created;

    public event EventHandler<ApplicationState>? StateChanged;

    /// <summary>
    /// 推进到指定状态；与当前相同则忽略。变更在锁外广播，避免订阅者回调时持锁。
    /// 订阅者异常逐个隔离并记录：状态广播是宿主启停编排的一部分，任一订阅者失败
    /// 不得中断后续订阅者，更不得让宿主停在「模块在跑但状态机已切换」的半完成状态。
    /// </summary>
    public void MoveTo(ApplicationState state)
    {
        lock (_gate)
        {
            if (State == state) return;
            State = state;
        }

        var handlers = StateChanged;
        if (handlers is null) return;

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<ApplicationState>>())
        {
            try
            {
                handler(this, state);
            }
            catch (Exception exception)
            {
                logger.LogError(StateSubscriberFailed, exception,
                    "应用状态订阅者在处理 {ApplicationState} 时抛出异常，已隔离并继续通知其余订阅者。", state);
            }
        }
    }
}
