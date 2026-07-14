using WhiteBoard.Framework.Hosting.Modules;

namespace WhiteBoard.Framework.Hosting.Internal;

/// <summary>
/// 模块依赖图的稳定拓扑排序。依赖优先；没有依赖关系的模块保持最初注册顺序，
/// 使启动顺序可预测且不因实现细节漂移。
/// </summary>
internal static class ModuleGraphSorter
{
    public static IReadOnlyList<ModuleRegistration> Sort(IReadOnlyList<ModuleRegistration> modules)
    {
        var byType = new Dictionary<Type, ModuleRegistration>();
        foreach (var module in modules)
        {
            if (!byType.TryAdd(module.ModuleType, module))
                throw new DuplicateModuleException(module.ModuleType);
        }

        var dependents = modules.ToDictionary(m => m.ModuleType, _ => new List<ModuleRegistration>());
        var remainingDependencies = modules.ToDictionary(m => m.ModuleType, m => m.Descriptor.Dependencies.Count);
        foreach (var module in modules)
        {
            foreach (var dependency in module.Descriptor.Dependencies)
            {
                if (!byType.TryGetValue(dependency, out var dependencyModule))
                    throw new MissingModuleDependencyException(module.ModuleType, dependency);
                dependents[dependencyModule.ModuleType].Add(module);
            }
        }

        // 按最初登记位置取可执行节点，满足无关模块顺序稳定。
        var ready = new PriorityQueue<ModuleRegistration, int>();
        foreach (var module in modules.Where(m => remainingDependencies[m.ModuleType] == 0))
            ready.Enqueue(module, module.RegistrationIndex);

        var ordered = new List<ModuleRegistration>(modules.Count);
        while (ready.TryDequeue(out var module, out _))
        {
            ordered.Add(module);
            foreach (var dependent in dependents[module.ModuleType])
            {
                remainingDependencies[dependent.ModuleType]--;
                if (remainingDependencies[dependent.ModuleType] == 0)
                    ready.Enqueue(dependent, dependent.RegistrationIndex);
            }
        }

        if (ordered.Count != modules.Count)
            throw FindCycle(modules, byType, remainingDependencies);

        return ordered;
    }

    private static ModuleDependencyCycleException FindCycle(
        IReadOnlyList<ModuleRegistration> modules,
        IReadOnlyDictionary<Type, ModuleRegistration> byType,
        IReadOnlyDictionary<Type, int> remainingDependencies)
    {
        var unresolved = new HashSet<Type>(remainingDependencies
            .Where(pair => pair.Value > 0)
            .Select(pair => pair.Key));
        var states = new Dictionary<Type, VisitState>();
        var path = new List<Type>();

        foreach (var module in modules)
        {
            if (!unresolved.Contains(module.ModuleType)) continue;
            var cycle = FindCycle(module, byType, unresolved, states, path);
            if (cycle is not null) return new ModuleDependencyCycleException(cycle);
        }

        throw new InvalidOperationException("模块依赖图无法排序，但未能定位循环路径。");
    }

    private static IReadOnlyList<Type>? FindCycle(ModuleRegistration module,
        IReadOnlyDictionary<Type, ModuleRegistration> byType,
        IReadOnlySet<Type> unresolved,
        IDictionary<Type, VisitState> states,
        List<Type> path)
    {
        var type = module.ModuleType;
        if (states.TryGetValue(type, out var state))
        {
            if (state == VisitState.Done) return null;
            if (state == VisitState.Visiting)
            {
                var start = path.FindIndex(t => t == type);
                return path.Skip(start).Append(type).ToList();
            }
        }

        states[type] = VisitState.Visiting;
        path.Add(type);
        foreach (var dependency in module.Descriptor.Dependencies)
        {
            if (!unresolved.Contains(dependency)) continue;
            var cycle = FindCycle(byType[dependency], byType, unresolved, states, path);
            if (cycle is not null) return cycle;
        }
        path.RemoveAt(path.Count - 1);
        states[type] = VisitState.Done;
        return null;
    }

    private enum VisitState { Visiting, Done }
}
