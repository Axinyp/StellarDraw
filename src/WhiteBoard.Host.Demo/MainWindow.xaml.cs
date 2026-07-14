using System.IO;
using System.Text.Json;
using System.Windows;
using WhiteBoard.AI.Providers;
using WhiteBoard.AI.Providers.ImageGen;
using WhiteBoard.AI.Session;
using WhiteBoard.AI.Settings;
using WhiteBoard.AI.Voice;
using WhiteBoard.Core.Domain;
using WhiteBoard.Mcp.Core.Client;
using WhiteBoard.Mcp.Core.Hosting;
using WhiteBoard.Mcp.Core.Registry;
using WhiteBoard.Mcp.Core.Server;
using WhiteBoard.UI.ViewModels;

namespace WhiteBoard.Host.Demo;

public partial class MainWindow : Window
{
    private readonly PluginManager _manager = new();
    private readonly InMemoryMessageBus _bus = new();
    private readonly MockHostCapabilities _host = MockHostCapabilities.Instance;

    private static readonly GridLength AiPanelWidth = new(320, GridUnitType.Pixel);

    private WhiteboardAISession? _aiSession;
    private AiChatViewModel? _aiChatVm;
    private AiSettingsViewModel? _settingsVm;
    private McpSessionRegistry? _mcpRegistry;
    private EmbeddedMcpServerHost? _mcpHost;
    private McpClientProvider? _mcpClientProvider;

    /// <summary>最近一次已应用的设置快照，用于保存时做分区差异比对（避免非 MCP 变更也重签 Token）。</summary>
    private AIWhiteboardSettings? _currentSettings;

    public MainWindow()
    {
        InitializeComponent();
        Closed += async (_, _) => await TearDownAsync();
        Loaded += async (_, _) => await AutoLoadAsync();
        PluginHost.SizeChanged += OnPluginHostSizeChanged;
    }

    // ── AI 面板切换 ───────────────────────────────────────────────────────────

    private void OnAiToggleChecked(object sender, RoutedEventArgs e)
        => ColAi.Width = AiPanelWidth;

    private void OnAiToggleUnchecked(object sender, RoutedEventArgs e)
        => ColAi.Width = new GridLength(0);

    // ── 画布尺寸同步 ──────────────────────────────────────────────────────────

    private void OnPluginHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var session = _manager.GetExportedSession();
        if (session is null) return;
        session.CanvasWidth = (float)e.NewSize.Width;
        session.CanvasHeight = (float)e.NewSize.Height;
    }

    // ── 自动加载 ──────────────────────────────────────────────────────────────

    private async Task AutoLoadAsync()
    {
        var dll = FindPluginDll();
        if (dll is null)
        {
            SetStatus("未找到插件 DLL");
            TxtInfo.Text = $"请将 WhiteBoard.Plugin.dll 放入 {DefaultPluginDir()} 目录";
            return;
        }

        await LoadFromPathAsync(dll);
    }

    private static string? FindPluginDll()
    {
        var base_ = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(base_, "plugins", "whiteboard", "WhiteBoard.Plugin.dll"),
            Path.Combine(base_, "WhiteBoard.Plugin.dll"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string DefaultPluginDir()
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "whiteboard");

    // ── 重新加载插件 ──────────────────────────────────────────────────────────

    private async void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        BtnReload.IsEnabled = false;
        SetStatus("正在重新加载…");

        try
        {
            PluginHost.Child = null;
            await DisposeAiSessionAsync();
            await _manager.UnloadAsync();

            var dll = FindPluginDll();
            if (dll is null)
            {
                SetStatus("未找到插件 DLL，请先构建 WhiteBoard.Plugin");
                return;
            }

            await LoadFromPathAsync(dll);
        }
        catch (Exception ex)
        {
            SetStatus($"重新加载失败: {ex.Message}");
            MessageBox.Show(ex.ToString(), "加载错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnReload.IsEnabled = true;
        }
    }

    // ── 核心加载逻辑 ──────────────────────────────────────────────────────────

    private async Task LoadFromPathAsync(string dllPath)
    {
        SetStatus("加载中…");
        try
        {
            await _manager.LoadAsync(dllPath, _bus, _host);

            if (_manager.ViewHandle?.View is { } view)
                PluginHost.Child = view;

            var id = _manager.ViewHandle?.PluginId ?? "unknown";
            SetStatus($"插件已加载: {id}");
            TxtInfo.Text = $"DLL: {Path.GetFileName(dllPath)}  |  ALC: WhiteboardPlugin";

            // Wire up AI session after plugin is loaded
            var session = _manager.GetExportedSession();
            if (session is not null)
                await InitAiSessionAsync(session);
            else
                TxtInfo.Text += "  |  ⚠ AI: 未能获取 Session";
        }
        catch (Exception ex)
        {
            SetStatus($"加载失败: {ex.Message}");
            MessageBox.Show(ex.ToString(), "加载错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── AI 会话初始化 ─────────────────────────────────────────────────────────

    /// <summary>首次加载：从磁盘读取设置，构建会话核心，并按需启动 MCP 服务端子系统。</summary>
    private async Task InitAiSessionAsync(WhiteboardSession session)
    {
        var settings = LoadSettings();
        await BuildAiSessionCoreAsync(session, settings);
        _currentSettings = settings;

        // MCP 内嵌服务（含 token 签发与 UI 回写）
        if (settings.McpServer.Enabled)
            await InitMcpAsync(settings);
    }

    /// <summary>
    /// 构建「会话核心」：LLM、图像生成、语音管道、外部 MCP 客户端工具、聊天 VM 与设置 VM。
    /// 不涉及 MCP 服务端子系统（registry/host/token），后者由 <see cref="InitMcpAsync"/> 单独管理，
    /// 以便设置保存时能保留已签发的 Token。
    /// </summary>
    private async Task BuildAiSessionCoreAsync(WhiteboardSession session, AIWhiteboardSettings settings)
    {
        var secrets = new EnvSecretStore();
        var llmProvider = new SettingsBackedLlmProvider(settings, secrets);

        // 以 PluginHost 当前实际尺寸初始化画布大小，后续通过 SizeChanged 持续同步
        if (PluginHost.ActualWidth > 0 && PluginHost.ActualHeight > 0)
        {
            session.CanvasWidth = (float)PluginHost.ActualWidth;
            session.CanvasHeight = (float)PluginHost.ActualHeight;
        }

        _settingsVm = new AiSettingsViewModel();
        _settingsVm.LoadFrom(settings);
        _settingsVm.TestConnectionRequested += OnTestConnectionRequested;

        // 图像生成 Provider：未启用 / 未配置 Key 时为 null，generate_and_insert_image 工具不注册
        var imageGen = ImageGenProviderFactory.Create(settings, secrets);

        _aiSession = new WhiteboardAISession(llmProvider, session, imageGen: imageGen);

        // #1 语音管道：启用时按设置构建 STT（whisper/windows）与可选唤醒词，挂载到会话
        if (settings.SttEnabled)
            AttachVoiceSafe(settings, secrets);

        _aiChatVm = new AiChatViewModel(_aiSession, _settingsVm);
        _aiChatVm.SettingsChanged += OnSettingsChanged;
        AiPanel.DataContext = _aiChatVm;

        // #5 外部 MCP 客户端：把已配置的外部 MCP server 工具并入本会话工具集
        await AttachMcpClientToolsAsync(settings);

        TxtInfo.Text += $"  |  AI: {settings.LlmProviderType}/{settings.LlmModel}";
        if (imageGen is not null)
            TxtInfo.Text += $"  |  图像: {settings.ImageGenProviderType}";
    }

    /// <summary>按设置构建并挂载语音管道；任何构建失败（如不支持的 provider）仅提示，不影响其余功能。</summary>
    private void AttachVoiceSafe(AIWhiteboardSettings settings, ISecretStore secrets)
    {
        if (_aiSession is null) return;
        try
        {
            var stt = VoiceProviderFactory.CreateStt(settings, secrets);

            // 唤醒词：勾选「启用唤醒词」即挂载检测器（不再额外要求 VoiceMode=wake-word）。
            // CreateWakeWordDetector 在未启用时返回 null。
            var wake = VoiceProviderFactory.CreateWakeWordDetector(settings);

            _aiSession.AttachVoice(
                stt,
                settings.SttLanguage,
                wakeWordDetector: wake,
                utteranceTimeout: TimeSpan.FromSeconds(settings.WakeWordUtteranceSeconds));

            // 把引擎初始化失败（模型缺失 / 缺少语言包等）显式提示，避免"静默无反应"。
            if (stt is LocalWhisperSttProvider { InitializationError: { } lwErr })
                SetStatus(lwErr);
            else if (stt is WindowsSpeechSttProvider { InitializationError: { } sttErr })
                SetStatus(sttErr);
            else if (wake is VadWhisperWakeWordDetector { InitializationError: { } vadErr })
                SetStatus(vadErr);
            else if (wake is WindowsSpeechWakeWordDetector { InitializationError: { } wakeErr })
                SetStatus(wakeErr);
            else
                SetStatus($"语音已就绪: {settings.SttProviderType}"
                    + (wake is not null ? $" + 唤醒词「{settings.WakeWord}」" : ""));
        }
        catch (Exception ex)
        {
            SetStatus($"语音初始化失败: {ex.Message}");
        }
    }

    /// <summary>连接已配置且启用的外部 MCP server，拉取其工具并注册进本会话（前缀隔离命名）。</summary>
    private async Task AttachMcpClientToolsAsync(AIWhiteboardSettings settings)
    {
        var enabled = settings.McpClient.Connections.Where(c => c.Enabled).ToList();
        if (_aiSession is null || enabled.Count == 0) return;

        try
        {
            _mcpClientProvider = new McpClientProvider();
            var specs = enabled.Select(ToConnectionSpec).ToList();
            await _mcpClientProvider.InitializeAsync(specs);

            var regs = await _mcpClientProvider.GetToolRegistrationsAsync();
            if (regs.Count > 0)
            {
                _aiSession.RegisterExternalTools(
                    regs.Select(r => (r.Function,
                                      r.Policy.RequiresConfirmation,
                                      r.Policy.IsDestructive)).ToList());

                if (_aiChatVm is not null)
                    _aiChatVm.McpActiveConnections = regs.Select(r => r.ConnectionId).Distinct().Count();

                TxtInfo.Text += $"  |  外部MCP工具: {regs.Count}";
            }
        }
        catch (Exception ex)
        {
            SetStatus($"MCP 客户端连接失败: {ex.Message}");
        }
    }

    private static ExternalMcpConnectionSpec ToConnectionSpec(McpClientConnectionSettings c) => new()
    {
        ConnectionId = c.ConnectionId,
        DisplayName = c.DisplayName,
        Enabled = c.Enabled,
        Transport = c.Transport,
        Command = c.Command,
        Arguments = c.Arguments,
        WorkingDirectory = c.WorkingDirectory,
        Endpoint = c.Endpoint,
        ToolNamePrefix = c.ToolNamePrefix,
        TrustLevel = c.TrustLevel,
        AllowDestructiveTools = c.AllowDestructiveTools,
    };

    /// <summary>
    /// 启动 MCP 内嵌服务并接通 token 链路：注入 token 工厂、订阅签发事件回写设置 UI、
    /// 接通「重新生成」命令，并签发首个 token。HTTP 传输按需启动 Kestrel。
    /// </summary>
    private async Task InitMcpAsync(AIWhiteboardSettings settings)
    {
        if (_aiSession is null || _settingsVm is null) return;

        _mcpRegistry = new McpSessionRegistry();
        var factory = new McpSessionRegistryTokenFactory(_mcpRegistry, _aiSession);
        _aiSession.SetMcpTokenFactory(factory);
        WireMcpTokenHandlers();

        // 启动内嵌 HTTP MCP server（供 Cursor 等 HTTP 客户端连接）
        if (settings.McpServer.HttpEnabled)
        {
            try
            {
                _mcpHost = new EmbeddedMcpServerHost();
                await _mcpHost.StartAsync(new EmbeddedMcpServerOptions
                {
                    Url = settings.McpServer.HttpUrl,
                    RoutePrefix = settings.McpServer.RoutePrefix,
                    EnableLegacySse = settings.McpServer.EnableLegacySse,
                }, _mcpRegistry, new McpToolBridge());
            }
            catch (Exception ex)
            {
                SetStatus($"MCP 服务启动失败: {ex.Message}");
                _mcpHost = null;
            }
        }

        // 签发首个 token（即便 HTTP 未启用，stdio/管道宿主仍需 token），触发上面的 UI 回写。
        try
        {
            await _aiSession.IssueMcpTokenAsync(
                TimeSpan.FromMinutes(settings.McpServer.TokenLifetimeMinutes),
                settings.McpServer.AllowDestructiveTools);
        }
        catch (Exception ex) { SetStatus($"MCP Token 签发失败: {ex.Message}"); }
    }

    /// <summary>把 token 签发事件与「重新生成」命令接到当前 <see cref="_aiSession"/> / <see cref="_settingsVm"/>。</summary>
    private void WireMcpTokenHandlers()
    {
        if (_aiSession is null || _settingsVm is null) return;

        // token 签发后回写「当前 Token」（事件可能来自后台线程，切回 UI 线程）。
        // _aiSession / _settingsVm 每次重建整体替换，故闭包订阅无需显式退订。
        _aiSession.McpTokenIssued += (_, token) =>
            Dispatcher.Invoke(() => _settingsVm!.CurrentMcpToken = token);

        // 「重新生成」按钮：IssueMcpTokenAsync 内部会先撤销旧 token 再签发新 token。
        _settingsVm.RegenerateMcpTokenRequested += async (_, _) =>
        {
            try
            {
                if (_aiSession is not null)
                    await _aiSession.IssueMcpTokenAsync(
                        TimeSpan.FromMinutes(_settingsVm!.McpTokenLifetimeMinutes),
                        _settingsVm.McpAllowDestructiveTools);
            }
            catch (Exception ex) { SetStatus($"重新生成 Token 失败: {ex.Message}"); }
        };
    }

    private async void OnSettingsChanged(object? sender, AIWhiteboardSettings settings)
    {
        var old = _currentSettings;
        SaveSettings(settings);

        var session = _manager.GetExportedSession();
        if (session is null) return;

        // 仅当 MCP 服务端分区真正变化才整体重建 MCP（会重签 token，属预期）；
        // 其余分区（LLM/语音/图像/MCP 客户端）变化时保留 MCP 服务端与已签发 Token。
        bool mcpServerChanged = old is null || McpServerSectionChanged(old.McpServer, settings.McpServer);

        if (mcpServerChanged)
        {
            await DisposeAiSessionAsync();
            await InitAiSessionAsync(session);
        }
        else
        {
            await RebuildAiSessionPreservingMcpAsync(session, settings);
        }

        SetStatus($"设置已保存并应用: {settings.LlmProviderType}/{settings.LlmModel}");
    }

    /// <summary>
    /// 重建会话核心但保留 MCP 服务端子系统与已签发 Token：
    /// 先把 Token 从旧会话「摘下」（避免 Dispose 撤销），重建会话后把 Token 重绑到新会话。
    /// </summary>
    private async Task RebuildAiSessionPreservingMcpAsync(WhiteboardSession session, AIWhiteboardSettings settings)
    {
        // 1. 摘下 Token，使旧会话 Dispose 时不撤销它
        var token = _aiSession?.DetachMcpTokenForHandoff();

        // 2. 拆除会话核心（聊天 VM / 外部 MCP 客户端 / 会话），但不动 _mcpHost / _mcpRegistry
        await DisposeAiSessionCoreAsync();

        // 3. 重建会话核心（LLM/语音/图像/MCP 客户端 + 聊天 VM + 设置 VM）
        await BuildAiSessionCoreAsync(session, settings);

        // 4. 把 MCP 服务端子系统重新接到新会话，并移交既有 Token（保持 Token 不变）
        if (_mcpRegistry is not null && _aiSession is not null && _settingsVm is not null)
        {
            var factory = new McpSessionRegistryTokenFactory(_mcpRegistry, _aiSession);
            _aiSession.SetMcpTokenFactory(factory);
            WireMcpTokenHandlers();

            if (token is not null)
            {
                _mcpRegistry.RebindActiveSessions(_aiSession);
                _aiSession.AdoptMcpToken(token);
                _settingsVm.CurrentMcpToken = token;
            }
        }

        _currentSettings = settings;
    }

    /// <summary>比较 MCP 服务端设置分区是否发生变化。</summary>
    private static bool McpServerSectionChanged(McpServerSettings a, McpServerSettings b)
        => a.Enabled != b.Enabled
        || a.StdioEnabled != b.StdioEnabled
        || a.HttpEnabled != b.HttpEnabled
        || a.EnableLegacySse != b.EnableLegacySse
        || a.HttpUrl != b.HttpUrl
        || a.RoutePrefix != b.RoutePrefix
        || a.TokenLifetimeMinutes != b.TokenLifetimeMinutes
        || a.AllowDestructiveTools != b.AllowDestructiveTools;

    private async void OnTestConnectionRequested(object? sender, AIWhiteboardSettings settings)
    {
        if (sender is not AiSettingsViewModel vm) return;
        vm.IsTestingConnection = true;
        vm.ConnectionTestStatus = "正在连接…";
        try
        {
            var secrets = new EnvSecretStore();
            var provider = new SettingsBackedLlmProvider(settings, secrets);
            using var client = provider.CreateClient();
            await client.GetResponseAsync(
                [new Microsoft.Extensions.AI.ChatMessage(
                    Microsoft.Extensions.AI.ChatRole.User, "ping")],
                cancellationToken: CancellationToken.None);
            vm.ConnectionTestStatus = "✓ 连接成功";
        }
        catch (Exception ex)
        {
            vm.ConnectionTestStatus = $"✗ {ex.Message}";
        }
        finally
        {
            vm.IsTestingConnection = false;
        }
    }

    /// <summary>拆除会话核心（聊天 VM、外部 MCP 客户端、AI 会话），不触碰 MCP 服务端子系统。</summary>
    private async Task DisposeAiSessionCoreAsync()
    {
        if (_aiChatVm is not null)
        {
            _aiChatVm.SettingsVm.TestConnectionRequested -= OnTestConnectionRequested;
            _aiChatVm.SettingsChanged -= OnSettingsChanged;
            _aiChatVm.Dispose();
            _aiChatVm = null;
        }
        _settingsVm = null;

        if (_mcpClientProvider is not null)
        {
            await _mcpClientProvider.DisposeAsync();
            _mcpClientProvider = null;
        }

        // 会话 Dispose 会撤销其当前 Token；调用方若要保留 Token，须先 DetachMcpTokenForHandoff。
        if (_aiSession is not null)
        {
            await _aiSession.DisposeAsync();
            _aiSession = null;
        }

        AiPanel.DataContext = null;
    }

    /// <summary>完整拆除：会话核心 + MCP 服务端子系统（host、registry）。</summary>
    private async Task DisposeAiSessionAsync()
    {
        // 先停 HTTP server（停止接受新连接）；再 dispose 会话（其内部会撤销 token，
        // 需要 registry 仍存活）；最后才 dispose registry。
        if (_mcpHost is not null)
        {
            await _mcpHost.DisposeAsync();
            _mcpHost = null;
        }

        await DisposeAiSessionCoreAsync();

        _mcpRegistry?.Dispose();
        _mcpRegistry = null;
    }

    private async Task TearDownAsync()
    {
        await DisposeAiSessionAsync();
        await _manager.DisposeAsync();
    }

    // ── 设置持久化 ────────────────────────────────────────────────────────────

    private static string SettingsFilePath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.local.json");

    private static AIWhiteboardSettings LoadSettings()
    {
        if (File.Exists(SettingsFilePath))
        {
            try
            {
                var json = File.ReadAllText(SettingsFilePath);
                var loaded = JsonSerializer.Deserialize<AIWhiteboardSettings>(json);
                if (loaded is not null) return loaded;
            }
            catch { /* 文件损坏则回退到默认 */ }
        }
        return BuildDefaultSettings();
    }

    private static void SaveSettings(AIWhiteboardSettings settings)
    {
        var json = JsonSerializer.Serialize(settings,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsFilePath, json);
    }

    private static AIWhiteboardSettings BuildDefaultSettings() => new()
    {
        LlmProviderType = Environment.GetEnvironmentVariable("WHITEBOARD_LLM_PROVIDER") ?? "ollama",
        LlmEndpoint = Environment.GetEnvironmentVariable("WHITEBOARD_LLM_ENDPOINT") ?? "http://localhost:11434",
        LlmModel = Environment.GetEnvironmentVariable("WHITEBOARD_LLM_MODEL") ?? "qwen2.5",
        LlmApiKeyRef = Environment.GetEnvironmentVariable("WHITEBOARD_LLM_APIKEY_REF") ?? "",
        ExecutionMode = "ConfirmRequired",
    };

    private void SetStatus(string text) => TxtStatus.Text = text;
}

/// <summary>
/// Demo 用 ISecretStore：优先从环境变量读取密钥，找不到时直接将 key 本身当作密钥值返回。
/// 这样用户既可以填环境变量名（如 OPENAI_API_KEY），也可以直接填实际 API Key（如 sk-xxx）。
/// </summary>
internal sealed class EnvSecretStore : ISecretStore
{
    public string Load(string key)
        => Environment.GetEnvironmentVariable(key)
        ?? Environment.GetEnvironmentVariable(key.Replace(".", "_").ToUpperInvariant())
        ?? key;   // 兜底：直接将 keyRef 本身当密钥值使用

    public void Save(string key, string value)
    { }

    public void Delete(string key)
    { }

    public bool Exists(string key) => !string.IsNullOrEmpty(Load(key));
}