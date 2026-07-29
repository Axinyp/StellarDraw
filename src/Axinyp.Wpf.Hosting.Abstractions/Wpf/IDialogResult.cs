namespace Axinyp.Wpf.Hosting.Abstractions.Wpf;

/// <summary>
/// 对话框关闭结果。
/// </summary>
public interface IDialogResult
{
    /// <summary>
    /// 对话框结果值；通常 true 表示确认，false 表示取消，null 表示未定义。
    /// </summary>
    bool? Result { get; }

    /// <summary>
    /// 关闭时携带的附加负载。
    /// </summary>
    object? Payload { get; }
}

/// <summary>
/// 默认对话框结果实现。
/// </summary>
/// <param name="Result">对话框结果值。</param>
/// <param name="Payload">关闭时携带的附加负载。</param>
public sealed record DialogResult(bool? Result, object? Payload = null) : IDialogResult
{
    /// <summary>
    /// 创建一个确认结果。
    /// </summary>
    /// <param name="payload">附加负载。</param>
    public static DialogResult Ok(object? payload = null) => new(true, payload);

    /// <summary>
    /// 创建一个取消结果。
    /// </summary>
    /// <param name="payload">附加负载。</param>
    public static DialogResult Cancel(object? payload = null) => new(false, payload);
}
