namespace StellarDraw.Axinyp.Spike;

/// <summary>
/// 用于验证 Axinyp 区域注册与导航的无领域内容视图模型。
/// </summary>
public sealed class StatusRegionViewModel
{
    /// <summary>
    /// 显示在受 Axinyp 区域生命周期管理的状态区中的文本。
    /// </summary>
    public string Message => "此区域由 Axinyp IRegionManager 与 INavigationService 管理。";
}
