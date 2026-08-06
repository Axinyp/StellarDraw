using Axinyp.Wpf.Hosting.Abstractions.Modules;

namespace Axinyp.Wpf.Hosting.Tests;

/// <summary>
/// 「可成功加载的启动期插件」fixture：插件相关测试会把本测试程序集复制进插件目录
/// 作为入口程序集，本类型即其中唯一公开的 <see cref="IModule"/> 实现。
/// </summary>
public sealed class SampleStartupPluginModule : IModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
