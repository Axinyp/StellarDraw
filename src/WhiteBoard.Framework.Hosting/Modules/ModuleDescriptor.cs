namespace WhiteBoard.Framework.Hosting.Modules;

/// <summary>
/// 模块的静态描述：稳定 ID 与直接依赖的模块类型。
/// 依赖由具体类型标识，避免字符串名称在重构时静默失效。
/// </summary>
public sealed record ModuleDescriptor
{
    public ModuleDescriptor(string id, IReadOnlyList<Type>? dependencies = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("模块 ID 不能为空。", nameof(id));

        Id = id;
        Dependencies = dependencies ?? [];
    }

    public string Id { get; }

    public IReadOnlyList<Type> Dependencies { get; }
}
