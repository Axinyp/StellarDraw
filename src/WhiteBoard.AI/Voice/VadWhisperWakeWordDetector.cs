using System.Diagnostics;
using System.IO;
using System.Text;
using NAudio.Wave;
using TinyPinyin;
using WhiteBoard.AI.Abstractions;

namespace WhiteBoard.AI.Voice;

/// <summary>
/// 基于「能量 VAD 门控 + 本地 Whisper」的唤醒词检测器。
/// 常驻麦克风以 16kHz/16bit/单声道采集；用能量阈值判定语音活动，只在检测到一段完整语音后
/// 才用本地 Whisper 转录该片段并匹配唤醒词——避免对静音持续跑推理。
/// 完全离线、中文识别准确率高，复用 <see cref="WhisperModelCache"/> 中已加载的模型。
///
/// 局限：能量 VAD 在嘈杂环境可能误触发语音段（但仅在转录命中唤醒词时才真正触发）；
/// 唤醒到触发有约 1~2 秒延迟（等待句尾静音 + 转录）。
/// </summary>
public sealed class VadWhisperWakeWordDetector : IInlineCommandWakeWordDetector
{
    private static readonly WaveFormat Format = new(16000, 16, 1);

    private const int SilenceMs = 400;      // 语音后静音超过此值 → 判定一句结束（越小切段越快、延迟越低）
    private const int MinSpeechMs = 300;    // 过短片段忽略
    private const int MaxSpeechMs = 6000;   // 单句最长（保护）
    private const double EnergyThreshold = 0.012; // 归一化 RMS 阈值(0..1)

    private static readonly int MinSpeechBytes = Format.AverageBytesPerSecond * MinSpeechMs / 1000;

    private readonly Whisper.net.WhisperFactory? _factory;
    private readonly string _language;
    private readonly string _normalizedWakeWord;
    private readonly string _wakeWordPinyin;
    private readonly int _armWindowMs;          // 唤醒后等待命令语音的时限（可配置）

    private WaveInEvent? _capture;
    private readonly List<byte> _segment = [];
    private readonly Queue<byte[]> _preRoll = new();   // 语音起始前的音频，避免起音被切
    private int _silenceAccumMs;
    private bool _inSpeech;

    private readonly object _procLock = new();
    private readonly Queue<byte[]> _pending = new();  // 待转录段队列（忙时入队，不丢弃）
    private bool _processing;

    private readonly object _armLock = new();
    private bool _armed;                        // 是否处于"待命令"窗口
    private Timer? _armTimer;                    // 超时未收到命令 → 自动退出唤醒态
    private CancellationTokenSource? _cts;

    public string WakeWord { get; }
    public bool IsListening { get; private set; }

    /// <summary>初始化失败原因（模型缺失/加载失败）；null 表示成功。</summary>
    public string? InitializationError { get; }

    public bool IsAvailable => _factory is not null;

    public event EventHandler? WakeWordDetected;

    public event EventHandler<string>? CommandRecognized;

    public event EventHandler? WakeWordExpired;

    /// <param name="wakeWord">唤醒词。</param>
    /// <param name="modelPath">ggml 模型路径。</param>
    /// <param name="language">识别语言。</param>
    /// <param name="armWindowSeconds">唤醒后等待命令语音的秒数；超时自动退出唤醒态。</param>
    public VadWhisperWakeWordDetector(string wakeWord, string modelPath,
        string language = "zh", int armWindowSeconds = 8)
    {
        WakeWord = wakeWord;
        _normalizedWakeWord = Normalize(wakeWord);
        _wakeWordPinyin = ToPinyin(wakeWord);
        _language = string.IsNullOrWhiteSpace(language) ? "zh" : language;
        _armWindowMs = System.Math.Max(1, armWindowSeconds) * 1000;
        _armTimer = new Timer(OnArmTimeout, null, Timeout.Infinite, Timeout.Infinite);

        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            InitializationError =
                $"唤醒词(VAD+Whisper)需要本地 Whisper 模型: '{modelPath}' 不存在，请在设置中指定 ggml 模型路径。";
            return;
        }

        try { _factory = WhisperModelCache.Get(modelPath); }
        catch (Exception ex) { InitializationError = $"唤醒词加载 Whisper 模型失败: {ex.Message}"; }
    }

    public void StartListening()
    {
        if (_factory is null || IsListening) return;

        _cts = new CancellationTokenSource();
        _capture = new WaveInEvent { WaveFormat = Format, BufferMilliseconds = 100 };
        _capture.DataAvailable += OnData;
        _segment.Clear();
        _preRoll.Clear();
        _inSpeech = false;
        _silenceAccumMs = 0;
        lock (_armLock) { _armed = false; StopArmTimer(); }
        _capture.StartRecording();
        IsListening = true;
    }

    public void StopListening()
    {
        if (!IsListening) return;
        IsListening = false;

        _cts?.Cancel();
        if (_capture is not null)
        {
            try { _capture.StopRecording(); } catch { /* 已停止 */ }
            _capture.DataAvailable -= OnData;
            _capture.Dispose();
            _capture = null;
        }
        _segment.Clear();
        _preRoll.Clear();
        _inSpeech = false;
        _silenceAccumMs = 0;
        lock (_procLock) _pending.Clear();
        lock (_armLock) { _armed = false; StopArmTimer(); }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        int chunkMs = (int)(e.BytesRecorded / (double)Format.AverageBytesPerSecond * 1000);
        bool voiced = Rms(e.Buffer, e.BytesRecorded) >= EnergyThreshold;

        if (voiced)
        {
            if (!_inSpeech)
            {
                _inSpeech = true;
                // 用 pre-roll 补上语音起始前的音频，避免"嘿"的起音被 VAD 切掉
                foreach (var pr in _preRoll) _segment.AddRange(pr);

                // 唤醒等待期间用户开始说话（多半是命令）→ 重置等待窗口，
                // 避免命令说到一半或转录尚未完成时窗口就超时退出。
                lock (_armLock)
                    if (_armed) _armTimer?.Change(_armWindowMs, Timeout.Infinite);
            }
            _silenceAccumMs = 0;
            Append(e.Buffer, e.BytesRecorded);
        }
        else if (_inSpeech)
        {
            _silenceAccumMs += chunkMs;
            Append(e.Buffer, e.BytesRecorded); // 保留尾部少量静音，利于识别完整
            if (_silenceAccumMs >= SilenceMs)
            {
                FinalizeSegment();
                UpdatePreRoll(e.Buffer, e.BytesRecorded);
                return;
            }
        }

        UpdatePreRoll(e.Buffer, e.BytesRecorded);

        // 超长保护：一直在说也强制切段
        if (_inSpeech && _segment.Count >= Format.AverageBytesPerSecond * MaxSpeechMs / 1000)
            FinalizeSegment();
    }

    /// <summary>维护约 200ms 的滚动 pre-roll（保留最近 2 个音频块）。</summary>
    private void UpdatePreRoll(byte[] buffer, int count)
    {
        var copy = new byte[count];
        Array.Copy(buffer, copy, count);
        _preRoll.Enqueue(copy);
        while (_preRoll.Count > 2) _preRoll.Dequeue();
    }

    private void Append(byte[] buffer, int count)
    {
        for (int i = 0; i < count; i++) _segment.Add(buffer[i]);
    }

    private void FinalizeSegment()
    {
        var pcm = _segment.ToArray();
        _segment.Clear();
        _inSpeech = false;
        _silenceAccumMs = 0;

        if (pcm.Length < MinSpeechBytes) return;

        double audioSec = pcm.Length / (double)Format.AverageBytesPerSecond;

        // 入队串行处理：转录忙时不丢弃（避免"命令段"在唤醒段转录期间被丢），
        // 仅在积压过多时丢最旧段做保护。保证唤醒段→命令段按顺序处理。
        int qlen;
        bool startLoop = false;
        lock (_procLock)
        {
            _pending.Enqueue(pcm);
            while (_pending.Count > 3) _pending.Dequeue();
            qlen = _pending.Count;
            if (!_processing) { _processing = true; startLoop = true; }
        }

        // 有新语音段入队：若在等待命令，重置超时（这段很可能就是命令，别让它在转录途中超时）
        lock (_armLock)
            if (_armed) _armTimer?.Change(_armWindowMs, Timeout.Infinite);

        Debug.WriteLine($"[WakeWord {Ts()}] 切段入队 | 音频 {audioSec:F1}s | 队列 {qlen}");

        if (startLoop) _ = Task.Run(ProcessQueueAsync);
    }

    private static string Ts() => DateTime.Now.ToString("HH:mm:ss.fff");

    private async Task ProcessQueueAsync()
    {
        var ct = _cts?.Token ?? CancellationToken.None;
        while (true)
        {
            byte[] pcm;
            lock (_procLock)
            {
                if (_pending.Count == 0) { _processing = false; return; }
                pcm = _pending.Dequeue();
            }
            if (ct.IsCancellationRequested) { lock (_procLock) _processing = false; return; }
            await TranscribeAndMatchAsync(pcm, ct).ConfigureAwait(false);
        }
    }

    private async Task TranscribeAndMatchAsync(byte[] pcm, CancellationToken ct)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var wav = PcmToWav(pcm);
            await using var processor = _factory!.CreateBuilder()
                .WithLanguage(_language)
                .WithThreads(System.Math.Max(1, Environment.ProcessorCount - 1)) // 多线程加速推理
                .WithPrompt(WakeWord)   // 提示词偏置：引导解码更倾向输出唤醒词
                .Build();

            using var ms = new MemoryStream(wav);
            var sb = new StringBuilder();
            await foreach (var seg in processor.ProcessAsync(ms, ct).ConfigureAwait(false))
                sb.Append(seg.Text);
            sw.Stop();

            var heard = sb.ToString().Trim();
            double audioSec = pcm.Length / (double)Format.AverageBytesPerSecond;
            if (ct.IsCancellationRequested || string.IsNullOrWhiteSpace(heard))
            {
                Debug.WriteLine($"[WakeWord {Ts()}] 空识别 | 音频 {audioSec:F1}s | 转录 {sw.ElapsedMilliseconds}ms");
                return;
            }

            bool isWake = Matches(heard);
            bool wasArmed;
            lock (_armLock) { wasArmed = _armed; }
            string action;

            if (wasArmed)
            {
                if (isWake)
                {
                    // 待命令期间又听到唤醒词 → 视为重复唤醒，重置等待窗口，不作为命令
                    lock (_armLock)
                        if (_armed) _armTimer?.Change(_armWindowMs, Timeout.Infinite);
                    action = "重复唤醒(重置等待)";
                }
                else
                {
                    // 唤醒后的正常命令
                    lock (_armLock) { _armed = false; StopArmTimer(); }
                    action = "命令 → 发送";
                    CommandRecognized?.Invoke(this, heard);
                }
            }
            else if (isWake)
            {
                WakeWordDetected?.Invoke(this, EventArgs.Empty);

                // 同一段里唤醒词之后还有内容 → 直接当命令；否则进入待命令窗口等下一段
                var command = ExtractCommandAfterWake(heard);
                if (!string.IsNullOrWhiteSpace(command))
                {
                    action = "唤醒+同段命令 → 发送";
                    CommandRecognized?.Invoke(this, command);
                }
                else
                {
                    lock (_armLock)
                    {
                        _armed = true;
                        _armTimer?.Change(_armWindowMs, Timeout.Infinite); // 启动超时倒计时
                    }
                    action = "唤醒(等待命令)";
                }
            }
            else
            {
                action = "忽略(未唤醒)";
            }

            // 日志显示"实际处理动作" + 音频时长 + 转录耗时(ms)，便于排查延迟
            Debug.WriteLine(
                $"[WakeWord {Ts()}] 听到: \"{heard}\" | 处理: {action} | 音频 {audioSec:F1}s | 转录 {sw.ElapsedMilliseconds}ms");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VadWhisperWakeWord] {ex.Message}");
        }
    }

    /// <summary>唤醒后超时未收到命令：退出唤醒态并通知 UI 清除"已唤醒"提示。</summary>
    private void OnArmTimeout(object? state)
    {
        // 仍有段在排队或转录中（很可能就是命令，且单次转录可能长达数秒）→ 延后判定，
        // 不在命令转录途中退出唤醒态。这是"识别到却被忽略"的根本修复。
        lock (_procLock)
        {
            if (_processing || _pending.Count > 0)
            {
                lock (_armLock)
                    if (_armed) _armTimer?.Change(_armWindowMs, Timeout.Infinite);
                return;
            }
        }

        bool wasArmed;
        lock (_armLock)
        {
            wasArmed = _armed;
            _armed = false;
        }
        if (wasArmed)
        {
            Debug.WriteLine($"[WakeWord {Ts()}] 唤醒超时，退出唤醒态");
            WakeWordExpired?.Invoke(this, EventArgs.Empty);
        }
    }

    private void StopArmTimer() => _armTimer?.Change(Timeout.Infinite, Timeout.Infinite);

    /// <summary>
    /// 匹配策略：先直接包含（严格）；否则转拼音做近似子串匹配，容忍 Whisper 的
    /// 同音/近音误识（如"嘿拜拜"↔"嘿白板"，拼音仅差 1）。
    /// </summary>
    private bool Matches(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var norm = Normalize(text);
        if (norm.Contains(_normalizedWakeWord, StringComparison.OrdinalIgnoreCase))
            return true;

        if (_wakeWordPinyin.Length == 0) return false;

        var heardPinyin = ToPinyin(text);
        if (heardPinyin.Length == 0) return false;

        // 近似子串编辑距离阈值：约 1/4 拼音长度，容错但不至于乱命中
        int threshold = System.Math.Max(1, _wakeWordPinyin.Length / 4);
        return ApproxContainsDistance(heardPinyin, _wakeWordPinyin) <= threshold;
    }

    /// <summary>转为无声调、无分隔的小写拼音（非中文字符保留其小写字母）。</summary>
    private static string ToPinyin(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var py = PinyinHelper.GetPinyin(s, ""); // 无分隔符
        var sb = new StringBuilder(py.Length);
        foreach (var c in py)
            if (char.IsLetter(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>pattern 相对 text 任意子串的最小编辑距离（近似子串匹配；首尾对齐免费）。</summary>
    private static int ApproxContainsDistance(string text, string pattern)
    {
        int n = text.Length, m = pattern.Length;
        if (m == 0) return 0;
        var prev = new int[n + 1]; // 空模式匹配任意起点 → 全 0（允许自由起点）
        for (int i = 1; i <= m; i++)
        {
            var cur = new int[n + 1];
            cur[0] = i;
            for (int j = 1; j <= n; j++)
            {
                int cost = pattern[i - 1] == text[j - 1] ? 0 : 1;
                cur[j] = System.Math.Min(
                    System.Math.Min(prev[j] + 1, cur[j - 1] + 1),
                    prev[j - 1] + cost);
            }
            prev = cur;
        }
        int min = int.MaxValue;
        for (int j = 0; j <= n; j++) min = System.Math.Min(min, prev[j]);
        return min;
    }

    /// <summary>
    /// 从命中唤醒词的同一段文本里提取唤醒词之后的命令部分。
    /// 仅当能明确定位唤醒词（原文直接包含）时才切分；无法可靠切分（如仅拼音近似命中）
    /// 则返回空，交由"待命令窗口"用下一段语音作为命令，避免把误识文本当命令。
    /// </summary>
    private string ExtractCommandAfterWake(string heard)
    {
        int i = heard.IndexOf(WakeWord, StringComparison.Ordinal);
        if (i < 0) return "";
        var rest = heard[(i + WakeWord.Length)..].Trim(' ', '，', ',', '。', '、', '!', '！', '?', '？');
        // 太短视为无有效命令（多半只是唤醒词）
        return rest.Length >= 2 ? rest : "";
    }

    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (!char.IsWhiteSpace(c) && !char.IsPunctuation(c) && !char.IsSymbol(c))
                sb.Append(c);
        return sb.ToString();
    }

    private static double Rms(byte[] buffer, int count)
    {
        int samples = count / 2;
        if (samples == 0) return 0;
        double sum = 0;
        for (int i = 0; i + 1 < count; i += 2)
        {
            short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            double v = sample / 32768.0;
            sum += v * v;
        }
        return System.Math.Sqrt(sum / samples);
    }

    private static byte[] PcmToWav(byte[] pcm)
    {
        using var ms = new MemoryStream();
        using (var writer = new WaveFileWriter(ms, Format))
            writer.Write(pcm, 0, pcm.Length);
        return ms.ToArray(); // MemoryStream.ToArray 在 Dispose 后仍可用
    }

    public void Dispose()
    {
        StopListening();
        _armTimer?.Dispose();
        _armTimer = null;
    }
}
