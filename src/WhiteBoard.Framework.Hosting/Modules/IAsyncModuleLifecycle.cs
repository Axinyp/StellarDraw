namespace WhiteBoard.Framework.Hosting.Modules;

/// <summary>
/// 可选的模块异步运行时生命周期。模块服务注册完成、DI 容器构建后由框架按依赖顺序调用。
/// StopAsync 始终按相反顺序调用，以便先释放依赖方再释放被依赖方。
/// </summary>
public interface IAsyncModuleLifecycle
{
    /// <summary>初始化运行时资源；发生在启动任务和 StartAsync 前。</summary>
    Task InitializeAsync(ModuleContext context, CancellationToken cancellationToken);

    /// <summary>启动模块后台工作或对外能力。</summary>
    Task StartAsync(ModuleContext context, CancellationToken cancellationToken);

    /// <summary>优雅停止。应支持在初始化或启动仅部分完成时调用。</summary>
    Task StopAsync(ModuleContext context, CancellationToken cancellationToken);
}
