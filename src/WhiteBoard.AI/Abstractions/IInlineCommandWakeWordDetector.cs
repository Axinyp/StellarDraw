namespace WhiteBoard.AI.Abstractions;

/// <summary>
/// 自带命令识别的唤醒词检测器：检测器本身持续转录音频，唤醒命中后能直接给出
/// 「唤醒词之后的命令文本」，无需上层再开第二个录音器去捕获后续话语
/// （避免唤醒延迟导致的错过与双麦克风竞争）。
///
/// 由 VAD+Whisper 这类"整段转录再匹配"的检测器实现；SAPI 语法检测器不实现此接口，
/// 仍走 <see cref="IWakeWordDetector"/> + 上层独立录音的传统流程。
/// </summary>
public interface IInlineCommandWakeWordDetector : IWakeWordDetector
{
    /// <summary>唤醒后识别到的命令文本（同段唤醒词之后的内容，或紧随其后的下一段语音）。</summary>
    event EventHandler<string>? CommandRecognized;

    /// <summary>唤醒后在设定时限内未收到命令语音，自动退出唤醒态时触发（供 UI 清除"已唤醒"提示）。</summary>
    event EventHandler? WakeWordExpired;
}
