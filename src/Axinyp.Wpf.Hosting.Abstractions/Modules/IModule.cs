namespace Axinyp.Wpf.Hosting.Abstractions.Modules;

/// <summary>
/// 模块：框架「模块化 / 插件化」的基本装配单元。
/// 每个模块把自身所需的服务登记进 DI 容器，宿主通过
/// <c>builder.AddModule&lt;TModule&gt;()</c> 组合任意数量的模块，实现按需拼装。
/// </summary>
public interface IModule
{
    /// <summary>把本模块的服务注册进容器。</summary>
    /// <param name="services">DI 服务集合。</param>
    /// <param name="configuration">应用配置（可读取本模块所需配置节）。</param>
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
}
