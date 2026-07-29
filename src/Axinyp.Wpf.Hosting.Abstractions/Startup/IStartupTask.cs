namespace Axinyp.Wpf.Hosting.Abstractions.Startup;

/// <summary>
/// 应用启动任务。模块完成 InitializeAsync 后、任何模块 StartAsync 前依注册顺序串行执行。
/// 启动任务应尽快返回；长期后台工作应由模块 StartAsync 或 HostedService 承担。
/// </summary>
public interface IStartupTask
{
    /// <summary>
    /// 执行启动任务。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ExecuteAsync(CancellationToken cancellationToken);
}
