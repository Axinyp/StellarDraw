namespace WhiteBoard.Framework.Hosting.Modules;

/// <summary>
/// 一个已注册且已完成依赖排序的模块。
/// </summary>
public sealed record ModuleRegistration(IModule Module, ModuleDescriptor Descriptor, int RegistrationIndex)
{
    public Type ModuleType => Module.GetType();
}
