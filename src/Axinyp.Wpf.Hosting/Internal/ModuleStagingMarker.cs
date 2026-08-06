namespace Axinyp.Wpf.Hosting.Internal;

/// <summary>
/// 以单例实例注册进服务集合的装配哨兵：
/// AddModule 每暂存一个模块计数加一，BuildModularHost 装配完成后置 Materialized。
/// 宿主启动时若发现「有暂存模块但从未装配」，说明调用方用 builder.Build() 绕过了
/// BuildModularHost——模块的 ConfigureServices 从未执行，必须 fail-fast 而非静默缺功能。
/// </summary>
internal sealed class ModuleStagingMarker
{
    public int StagedCount { get; set; }

    public bool Materialized { get; set; }
}
