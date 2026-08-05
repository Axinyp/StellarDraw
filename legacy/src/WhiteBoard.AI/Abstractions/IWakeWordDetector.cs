namespace WhiteBoard.AI.Abstractions;

/// <summary>
/// 唤醒词检测器抽象。在后台持续监听麦克风，检测到唤醒词后触发 <see cref="WakeWordDetected"/>。
/// </summary>
public interface IWakeWordDetector : IDisposable
{
    /// <summary>当前配置的唤醒词（或短语）。</summary>
    string WakeWord { get; }

    /// <summary>是否正在后台监听。</summary>
    bool IsListening { get; }

    /// <summary>开始持续监听。</summary>
    void StartListening();

    /// <summary>停止监听。</summary>
    void StopListening();

    /// <summary>检测到唤醒词时触发。</summary>
    event EventHandler? WakeWordDetected;
}