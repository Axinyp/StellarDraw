using Axinyp.Wpf.Hosting.Abstractions.Modules;

namespace Axinyp.Wpf.Hosting.Internal;

/// <summary>Host 构建前暂存模块注册，确保能先计算完整依赖图再修改 DI 服务集合。</summary>
internal sealed class ModuleRegistry
{
    private readonly List<IModule> _modules = [];
    private bool _faulted;

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
        if (_faulted)
            throw new InvalidOperationException(
                "上一次模块装配中途失败，builder 的服务集合可能已包含部分模块注册；" +
                "重试会导致这些服务被重复注册。请重建 HostApplicationBuilder 后再构建宿主。");
        if (IsMaterialized)
            throw new InvalidOperationException("模块只能装配一次。");

        var pending = _modules.ToArray();
        var registrations = pending.Select((module, index) => new ModuleRegistration(
            module,
            GetDescriptor(module),
            index)).ToList();

        // 依赖排序在写入服务集合之前完成：排序类异常（重复/缺依赖/环）不会污染 builder，可安全重试。
        var sorted = ModuleGraphSorter.Sort(registrations);

        try
        {
            foreach (var registration in sorted)
                registration.Module.ConfigureServices(services, configuration);
        }
        catch
        {
            // ConfigureServices 中途失败后无法回滚已写入的服务注册，标记损坏禁止重试。
            _faulted = true;
            throw;
        }

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
