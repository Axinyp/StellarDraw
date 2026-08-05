namespace Axinyp.Wpf.Hosting.Modules;

/// <summary>模块声明了未注册的直接依赖。</summary>
public sealed class MissingModuleDependencyException(Type moduleType, Type dependencyType)
    : InvalidOperationException($"模块 '{moduleType.FullName}' 依赖未注册的模块 '{dependencyType.FullName}'。")
{
    /// <summary>
    /// 声明依赖的模块类型。
    /// </summary>
    public Type ModuleType { get; } = moduleType;

    /// <summary>
    /// 缺失的依赖模块类型。
    /// </summary>
    public Type DependencyType { get; } = dependencyType;
}
