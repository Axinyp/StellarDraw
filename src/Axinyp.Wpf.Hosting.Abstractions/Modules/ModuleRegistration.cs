namespace Axinyp.Wpf.Hosting.Abstractions.Modules;

/// <summary>
/// 一个已注册且已完成依赖排序的模块。
/// </summary>
public sealed record ModuleRegistration(IModule Module, ModuleDescriptor Descriptor, int RegistrationIndex)
{
    /// <summary>
    /// 当前注册项对应的模块运行时类型。
    /// </summary>
    public Type ModuleType => Module.GetType();
}
