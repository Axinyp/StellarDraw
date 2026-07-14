namespace WhiteBoard.Framework.Hosting.Modules;

/// <summary>
/// 已排序模块目录。Host.Build 后冻结，供运行时生命周期、插件系统和诊断代码读取。
/// </summary>
public interface IModuleCatalog
{
    /// <summary>按依赖拓扑顺序排列的模块；无关模块保持注册顺序。</summary>
    IReadOnlyList<ModuleRegistration> Modules { get; }
}
