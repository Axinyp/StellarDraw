namespace WhiteBoard.Contracts.Plugin;

/// <summary>
/// 插件生命周期接口。宿主（Host）按以下顺序调用：
/// <list type="number">
///   <item><see cref="InitializeAsync"/>：初始化依赖（Session、SignalR、ViewModels），可访问宿主能力。</item>
///   <item><see cref="StartAsync"/>：开始正式工作（启动后台任务、注册快捷键）。</item>
///   <item><see cref="StopAsync"/>：优雅停止（取消任务、刷新缓存），不释放资源。</item>
///   <item><see cref="IAsyncDisposable.DisposeAsync"/>：释放所有非托管/托管资源。</item>
/// </list>
/// </summary>
public interface IPlugin : IAsyncDisposable
{
    /// <summary>插件的唯一反向域名标识，例如 "com.example.whiteboard"。</summary>
    string PluginId { get; }

    /// <summary>插件版本号（SemVer 格式）。</summary>
    string Version { get; }

    Task InitializeAsync(IPluginMessageBus bus, IHostCapabilities host, CancellationToken ct);

    Task StartAsync(CancellationToken ct);

    Task StopAsync(CancellationToken ct);
}