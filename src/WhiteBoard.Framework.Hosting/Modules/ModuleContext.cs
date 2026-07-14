namespace WhiteBoard.Framework.Hosting.Modules;

/// <summary>
/// 提供给模块异步生命周期的运行时上下文。
/// 模块不得在该阶段再修改 DI 注册；若需要作用域服务，请通过 <see cref="Services"/> 获取
/// <see cref="IServiceScopeFactory"/> 后自行创建作用域。
/// </summary>
public sealed class ModuleContext(ModuleRegistration registration, IConfiguration configuration,
    IServiceProvider services)
{
    public ModuleRegistration Registration { get; } = registration;

    public ModuleDescriptor Descriptor => Registration.Descriptor;

    public IConfiguration Configuration { get; } = configuration;

    public IServiceProvider Services { get; } = services;
}
