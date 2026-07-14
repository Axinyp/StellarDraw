namespace WhiteBoard.Framework.Hosting.Modules;

/// <summary>
/// 模块依赖描述提供者。模块实现此接口即可声明其身份和直接依赖；
/// 未实现时框架会使用模块类型全名作为身份，且视为无依赖。
/// </summary>
public interface IModuleDescriptorProvider
{
    /// <summary>返回当前模块的静态描述。必须在 Host.Build 前可安全调用。</summary>
    ModuleDescriptor GetDescriptor();
}
