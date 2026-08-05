namespace Axinyp.Wpf.Hosting.Abstractions.Modules;

/// <summary>
/// 只读模块目录，暴露完成依赖排序后的注册结果。
/// </summary>
public interface IModuleCatalog
{
    /// <summary>
    /// 所有已注册模块，顺序即最终初始化/启动顺序。
    /// </summary>
    IReadOnlyList<ModuleRegistration> Modules { get; }
}
