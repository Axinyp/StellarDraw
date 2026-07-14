using WhiteBoard.Framework.Hosting.Modules;

namespace WhiteBoard.Framework.Hosting.Internal;

/// <summary>Host 构建前暂存模块注册，确保能先计算完整依赖图再修改 DI 服务集合。</summary>
internal sealed class ModuleRegistry
{
    private readonly List<IModule> _modules = [];

    public bool IsMaterialized { get; private set; }

    public void Add(IModule module)
    {
        if (IsMaterialized)
            throw new InvalidOperationException("模块已完成装配，Host.Build 后不能再添加模块。");

        _modules.Add(module);
    }

    public IReadOnlyList<ModuleRegistration> Materialize(IServiceCollection services,
        IConfiguration configuration)
    {
        if (IsMaterialized)
            throw new InvalidOperationException("模块只能装配一次。");

        var pending = _modules.ToArray();
        var registrations = pending.Select((module, index) => new ModuleRegistration(
            module,
            GetDescriptor(module),
            index)).ToList();

        var sorted = ModuleGraphSorter.Sort(registrations);
        foreach (var registration in sorted)
            registration.Module.ConfigureServices(services, configuration);

        _modules.Clear();
        IsMaterialized = true;
        return sorted;
    }

    private static ModuleDescriptor GetDescriptor(IModule module)
    {
        if (module is IModuleDescriptorProvider provider)
            return provider.GetDescriptor();

        var type = module.GetType();
        return new ModuleDescriptor(type.FullName ?? type.Name);
    }
}
