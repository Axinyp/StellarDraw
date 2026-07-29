namespace Axinyp.Wpf.Hosting.Abstractions.Modules;

/// <summary>
/// 模块的静态描述：稳定 ID 与直接依赖的模块类型。
/// 依赖由具体类型标识，避免字符串名称在重构时静默失效。
/// </summary>
public sealed record ModuleDescriptor
{
    /// <summary>
    /// 创建模块描述。
    /// </summary>
    /// <param name="id">模块稳定 ID。</param>
    /// <param name="dependencies">模块的直接依赖类型列表。</param>
    public ModuleDescriptor(string id, IReadOnlyList<Type>? dependencies = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("模块 ID 不能为空。", nameof(id));

        Id = id;
        Dependencies = dependencies ?? [];
    }

    /// <summary>
    /// 模块稳定 ID。
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// 模块的直接依赖类型集合。
    /// </summary>
    public IReadOnlyList<Type> Dependencies { get; }
}
