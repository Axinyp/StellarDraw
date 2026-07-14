using Whisper.net;

namespace WhiteBoard.AI.Voice;

/// <summary>
/// 按模型文件路径共享 <see cref="WhisperFactory"/> 的进程级缓存。
/// PTT 识别（<see cref="LocalWhisperSttProvider"/>）与唤醒词检测
/// （<see cref="VadWhisperWakeWordDetector"/>）复用同一实例，避免同一模型被加载两份、翻倍内存。
/// 缓存持有工厂生命周期，调用方不应 Dispose 返回的工厂。
/// </summary>
internal static class WhisperModelCache
{
    private static readonly Dictionary<string, WhisperFactory> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();

    public static WhisperFactory Get(string modelPath)
    {
        lock (_lock)
        {
            if (!_cache.TryGetValue(modelPath, out var factory))
            {
                factory = WhisperFactory.FromPath(modelPath);
                _cache[modelPath] = factory;
            }
            return factory;
        }
    }
}
