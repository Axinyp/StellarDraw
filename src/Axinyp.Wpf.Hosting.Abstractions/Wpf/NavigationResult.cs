namespace Axinyp.Wpf.Hosting.Abstractions.Wpf;

/// <summary>导航操作结果。取消和失败均不会修改区域当前内容。</summary>
public sealed record NavigationResult(bool Succeeded, bool Cancelled = false, string? ErrorMessage = null)
{
    /// <summary>
    /// 成功完成导航。
    /// </summary>
    public static NavigationResult Success { get; } = new(true);

    /// <summary>
    /// 导航被当前 ViewModel 主动取消。
    /// </summary>
    public static NavigationResult CancelledByViewModel { get; } = new(false, true);

    /// <summary>
    /// 创建一个失败结果。
    /// </summary>
    /// <param name="message">失败原因。</param>
    public static NavigationResult Failed(string message) => new(false, false, message);
}
