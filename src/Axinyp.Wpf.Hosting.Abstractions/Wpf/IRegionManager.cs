using System.Windows.Controls;

namespace Axinyp.Wpf.Hosting.Abstractions.Wpf;

/// <summary>管理显式注册的 ContentControl 区域。区域注册在所属 Dispatcher 上进行，并在宿主卸载时释放。</summary>
public interface IRegionManager
{
    /// <summary>注册一个内容区域。调用方应保存返回值，并在宿主视图卸载时释放。</summary>
    IDisposable RegisterRegion(string name, ContentControl host);

    /// <summary>
    /// 判断指定名称的区域是否已注册。
    /// </summary>
    /// <param name="name">区域名称。</param>
    bool IsRegistered(string name);

    /// <summary>
    /// 获取指定名称的区域句柄。
    /// </summary>
    /// <param name="name">区域名称。</param>
    IRegion GetRegion(string name);
}
