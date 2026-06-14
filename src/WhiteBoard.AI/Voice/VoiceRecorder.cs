using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace WhiteBoard.AI.Voice;

/// <summary>
/// 麦克风录音器（Push-to-Talk 模式）。
/// 使用 NAudio WasapiCapture 捕获 PCM 音频，
/// StartRecording() → StopRecordingAsync() 返回完整 WAV 字节数组。
/// 线程安全：Start/Stop 可从任意线程调用。
/// </summary>
public sealed class VoiceRecorder : IDisposable
{
    private WasapiCapture? _capture;
    private MemoryStream? _buffer;
    private WaveFileWriter? _writer;
    private TaskCompletionSource<byte[]>? _tcs;
    private readonly object _lock = new();

    public bool IsRecording { get; private set; }

    /// <summary>开始录音。如果已在录音，先停止上一次。</summary>
    public void StartRecording()
    {
        lock (_lock)
        {
            StopCapture();

            _capture = new WasapiCapture();
            _buffer = new MemoryStream();
            _writer = new WaveFileWriter(_buffer, _capture.WaveFormat);
            _tcs = new TaskCompletionSource<byte[]>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            IsRecording = true;
        }
    }

    /// <summary>
    /// 停止录音并返回完整 WAV 字节数组。
    /// 如果未在录音，返回空数组。
    /// </summary>
    public async Task<byte[]> StopRecordingAsync(CancellationToken ct = default)
    {
        TaskCompletionSource<byte[]>? tcs;

        lock (_lock)
        {
            if (!IsRecording || _tcs is null) return [];
            tcs = _tcs;
            _capture?.StopRecording();
        }

        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        return await tcs.Task.ConfigureAwait(false);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_lock)
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (_lock)
        {
            _writer?.Flush();
            var data = _buffer?.ToArray() ?? [];
            _tcs?.TrySetResult(data);
            IsRecording = false;
        }
    }

    private void StopCapture()
    {
        _capture?.StopRecording();
        _writer?.Dispose();
        _writer = null;
        _buffer?.Dispose();
        _buffer = null;
        _capture?.Dispose();
        _capture = null;
        _tcs = null;
        IsRecording = false;
    }

    public void Dispose()
    {
        lock (_lock)
            StopCapture();
    }
}