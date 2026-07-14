using WhiteBoard.Framework.Hosting.Modules;

namespace WhiteBoard.Framework.Hosting.Internal;

/// <summary>冻结的模块目录实现。</summary>
internal sealed class ModuleCatalog(IReadOnlyList<ModuleRegistration> modules) : IModuleCatalog
{
    public IReadOnlyList<ModuleRegistration> Modules { get; } = modules;
}
