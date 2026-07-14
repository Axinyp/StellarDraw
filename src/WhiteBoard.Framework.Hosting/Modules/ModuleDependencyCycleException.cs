namespace WhiteBoard.Framework.Hosting.Modules;

/// <summary>模块依赖图中存在循环依赖。</summary>
public sealed class ModuleDependencyCycleException(IReadOnlyList<Type> cycle)
    : InvalidOperationException($"检测到模块循环依赖: {string.Join(" -> ", cycle.Select(t => t.Name))}")
{
    public IReadOnlyList<Type> Cycle { get; } = cycle;
}
