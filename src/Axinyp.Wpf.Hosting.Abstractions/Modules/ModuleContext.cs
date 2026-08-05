namespace Axinyp.Wpf.Hosting.Abstractions.Modules;

/// <summary>
/// 提供给模块异步生命周期的运行时上下文。
/// 模块不得在该阶段再修改 DI 注册；若需要作用域服务，请通过 <see cref="Services"/> 获取
/// <see cref="IServiceScopeFactory"/> 后自行创建作用域。
/// </summary>
public sealed class ModuleContext(ModuleRegistration registration, IConfiguration configuration,
    IServiceProvider services)
{
    /// <summary>
    /// 当前模块的注册信息。
    /// </summary>
    public ModuleRegistration Registration { get; } = registration;

    /// <summary>
    /// 当前模块的静态描述。
    /// </summary>
    public ModuleDescriptor Descriptor => Registration.Descriptor;

    /// <summary>
    /// 宿主配置根。
    /// </summary>
    public IConfiguration Configuration { get; } = configuration;

    /// <summary>
    /// 宿主根服务提供程序。
    /// </summary>
    public IServiceProvider Services { get; } = services;
}
