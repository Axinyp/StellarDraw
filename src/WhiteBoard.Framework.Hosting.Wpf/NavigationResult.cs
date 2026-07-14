namespace WhiteBoard.Framework.Hosting.Wpf;

/// <summary>导航操作结果。取消和失败均不会修改区域当前内容。</summary>
public sealed record NavigationResult(bool Succeeded, bool Cancelled = false, string? ErrorMessage = null)
{
    public static NavigationResult Success { get; } = new(true);

    public static NavigationResult CancelledByViewModel { get; } = new(false, true);

    public static NavigationResult Failed(string message) => new(false, false, message);
}
