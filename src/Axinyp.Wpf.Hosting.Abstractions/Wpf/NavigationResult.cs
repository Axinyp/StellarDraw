namespace Axinyp.Wpf.Hosting.Abstractions.Wpf;

/// <summary>导航操作结果。取消和失败均不会修改区域当前内容。</summary>
public sealed record NavigationResult(bool Succeeded, bool Cancelled = false, string? ErrorMessage = null)
{
    /// <summary>稳定失败分类；不会包含原始异常。</summary>
    public NavigationFailureKind FailureKind { get; init; }

    /// <summary>供 consumer 和日志关联的稳定诊断代码。</summary>
    public string? DiagnosticCode { get; init; }

    /// <summary>仅记录结构化日志事件编号，不持有日志或异常实例。</summary>
    public int? EventId { get; init; }

    /// <summary>仅记录异常类型名，不持有异常实例。</summary>
    public string? ExceptionType { get; init; }

    /// <summary>
    /// 成功完成导航。
    /// </summary>
    public static NavigationResult Success { get; } = new(true);

    /// <summary>
    /// 导航被当前 ViewModel 主动取消。
    /// </summary>
    public static NavigationResult CancelledByViewModel { get; } = new(false, true)
    {
        FailureKind = NavigationFailureKind.Cancelled,
        DiagnosticCode = "navigation.cancelled",
        EventId = WpfHostingEventIds.NavigationCancelled.Id
    };

    /// <summary>
    /// 创建一个失败结果。
    /// </summary>
    /// <param name="message">失败原因。</param>
    public static NavigationResult Failed(string message) => new(false, false, message);

    /// <summary>创建一个带稳定诊断信息的失败结果。</summary>
    public static NavigationResult Failed(
        string message,
        NavigationFailureKind failureKind,
        string diagnosticCode,
        string? exceptionType = null,
        int? eventId = null)
        => new(false, false, message)
        {
            FailureKind = failureKind,
            DiagnosticCode = diagnosticCode,
            ExceptionType = exceptionType,
            EventId = eventId
        };
}
