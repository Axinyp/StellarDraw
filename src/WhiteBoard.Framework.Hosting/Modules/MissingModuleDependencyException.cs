namespace WhiteBoard.Framework.Hosting.Modules;

/// <summary>模块声明了未注册的直接依赖。</summary>
public sealed class MissingModuleDependencyException(Type moduleType, Type dependencyType)
    : InvalidOperationException($"模块 '{moduleType.FullName}' 依赖未注册的模块 '{dependencyType.FullName}'。")
{
    public Type ModuleType { get; } = moduleType;

    public Type DependencyType { get; } = dependencyType;
}
