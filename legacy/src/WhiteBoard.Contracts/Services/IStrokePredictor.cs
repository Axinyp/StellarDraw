namespace WhiteBoard.Contracts.Services;

/// <summary>
/// 笔迹前向预测接口：基于最近若干确认点预测未来位置，用于降低触控输入延迟的感知。
/// 预测结果渲染在独立的 <see cref="UI.Controls.InkSurface"/> 第三层（_predictedVisual），
/// 下一帧确认点到来后立即清除并替换为实际轨迹。
/// </summary>
public interface IStrokePredictor
{
    /// <param name="recent">最近 5~8 个确认点（Document Space）</param>
    /// <param name="lookaheadMs">预测时间窗口；自适应 12~24ms，硬上限 32ms</param>
    /// <param name="isStrokeBegin">落笔第一帧，强制线性外推</param>
    IReadOnlyList<PredictedPoint> Predict(
        IReadOnlyList<PressurePoint> recent,
        float lookaheadMs = 16f,
        bool isStrokeBegin = false);
}

/// <summary>带时间戳的压感采样点（文档坐标系）。TimestampMs 用于速度估算。</summary>
public record PressurePoint(float X, float Y, float Pressure, long TimestampMs);

/// <summary>预测点：含置信度（0~1），值越低透明度越高，视觉上强调"预测不确定性"。</summary>
public record PredictedPoint(float X, float Y, float Pressure, float Confidence);