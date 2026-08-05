using System.Reflection;

namespace Axinyp.Wpf.Hosting.Internal;

/// <summary>
/// 轻量「俘获依赖(captive dependency)」检查：找出单例服务的构造函数直接依赖了作用域(Scoped)服务的情况——
/// 这类依赖会被单例长期持有，导致作用域语义失效，是 DI 中常见且隐蔽的缺陷。
/// 仅分析可反射的具体实现类型（工厂/实例注册、开放泛型无法静态分析，跳过）。
/// </summary>
internal static class ServiceLifetimeValidator
{
    public readonly record struct CaptiveDependency(Type Singleton, Type ScopedDependency)
    {
        public override string ToString()
            => $"单例 {Singleton.Name} 俘获了作用域服务 {ScopedDependency.Name}";
    }

    public static IReadOnlyList<CaptiveDependency> Validate(IServiceCollection services)
    {
        // 服务类型 → 生命周期（后注册覆盖先注册，与 MS DI 解析一致）。
        // keyed 注册整体跳过：其 ImplementationType 属性会抛异常，且按裸类型解析不到它们。
        var lifetimes = new Dictionary<Type, ServiceLifetime>();
        foreach (var d in services)
        {
            if (d.IsKeyedService) continue;
            lifetimes[d.ServiceType] = d.Lifetime;
        }

        var issues = new List<CaptiveDependency>();
        foreach (var d in services)
        {
            if (d.IsKeyedService || d.Lifetime != ServiceLifetime.Singleton) continue;
            var implType = d.ImplementationType;
            if (implType is null || implType.IsAbstract || implType.ContainsGenericParameters) continue;

            var ctor = GreediestConstructor(implType);
            if (ctor is null) continue;

            foreach (var p in ctor.GetParameters())
                if (lifetimes.TryGetValue(p.ParameterType, out var life) && life == ServiceLifetime.Scoped)
                    issues.Add(new CaptiveDependency(implType, p.ParameterType));
        }
        return issues;
    }

    private static ConstructorInfo? GreediestConstructor(Type type)
    {
        ConstructorInfo? best = null;
        foreach (var c in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            if (best is null || c.GetParameters().Length > best.GetParameters().Length)
                best = c;
        return best;
    }
}
