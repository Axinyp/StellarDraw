namespace WhiteBoard.Framework.Hosting.Modules;

/// <summary>同一 Host 中重复注册了相同模块类型。</summary>
public sealed class DuplicateModuleException(Type moduleType)
    : InvalidOperationException($"模块 '{moduleType.FullName}' 被重复注册。")
{
    public Type ModuleType { get; } = moduleType;
}
