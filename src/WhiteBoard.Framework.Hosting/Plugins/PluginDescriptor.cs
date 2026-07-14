namespace WhiteBoard.Framework.Hosting.Plugins;

/// <summary>已成功发现的启动期插件公开描述（不暴露 ALC、程序集或模块实例）。</summary>
public sealed record PluginDescriptor(
    string Id,
    string Version,
    string Directory,
    string EntryAssembly,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> ModuleTypes);
