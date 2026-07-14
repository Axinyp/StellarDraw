namespace WhiteBoard.Framework.Hosting.Wpf;

public interface IDialogResult { bool? Result { get; } object? Payload { get; } }
public sealed record DialogResult(bool? Result, object? Payload = null) : IDialogResult
{
    public static DialogResult Ok(object? payload = null) => new(true, payload);
    public static DialogResult Cancel(object? payload = null) => new(false, payload);
}
