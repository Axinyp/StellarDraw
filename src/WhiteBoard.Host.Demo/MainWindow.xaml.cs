using System.IO;
using System.Text.Json;
using System.Windows;
using WhiteBoard.AI.Providers;
using WhiteBoard.AI.Providers.ImageGen;
using WhiteBoard.AI.Session;
using WhiteBoard.AI.Settings;
using WhiteBoard.Core.Domain;
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
    private McpSessionRegistry? _mcpRegistry;
    private EmbeddedMcpServerHost? _mcpHost;

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

    private async Task InitAiSessionAsync(WhiteboardSession session)
    {
        // 同步路径：调用时已确保旧 session 已被 DisposeAiSessionAsync 清理
        var settings = LoadSettings();
        var secrets = new EnvSecretStore();
        var llmProvider = new SettingsBackedLlmProvider(settings, secrets);

        // 以 PluginHost 当前实际尺寸初始化画布大小，后续通过 SizeChanged 持续同步
        if (PluginHost.ActualWidth > 0 && PluginHost.ActualHeight > 0)
        {
            session.CanvasWidth = (float)PluginHost.ActualWidth;
            session.CanvasHeight = (float)PluginHost.ActualHeight;
        }

        var settingsVm = new AiSettingsViewModel();
        settingsVm.LoadFrom(settings);
        settingsVm.TestConnectionRequested += OnTestConnectionRequested;

        // 图像生成 Provider：未启用 / 未配置 Key 时为 null，generate_and_insert_image 工具不注册
        var imageGen = ImageGenProviderFactory.Create(settings, secrets);

        _aiSession = new WhiteboardAISession(llmProvider, session, imageGen: imageGen);
        _aiChatVm = new AiChatViewModel(_aiSession, settingsVm);
        _aiChatVm.SettingsChanged += OnSettingsChanged;
        AiPanel.DataContext = _aiChatVm;

        TxtInfo.Text += $"  |  AI: {settings.LlmProviderType}/{settings.LlmModel}";
        if (imageGen is not null)
            TxtInfo.Text += $"  |  图像: {settings.ImageGenProviderType}";

        // MCP 内嵌服务（含 token 签发与 UI 回写）
        if (settings.McpServer.Enabled)
            await InitMcpAsync(settings, settingsVm);
    }

    /// <summary>
    /// 启动 MCP 内嵌服务并接通 token 链路：注入 token 工厂、订阅签发事件回写设置 UI、
    /// 接通「重新生成」命令，并签发首个 token。HTTP 传输按需启动 Kestrel。
    /// </summary>
    private async Task InitMcpAsync(AIWhiteboardSettings settings, AiSettingsViewModel settingsVm)
    {
        if (_aiSession is null) return;

        _mcpRegistry = new McpSessionRegistry();
        var factory = new McpSessionRegistryTokenFactory(_mcpRegistry, _aiSession);
        _aiSession.SetMcpTokenFactory(factory);

        // token 签发后回写「当前 Token」（事件可能来自后台线程，切回 UI 线程）。
        // _aiSession / settingsVm 每次重建会整体替换，故此处闭包订阅无需显式退订。
        _aiSession.McpTokenIssued += (_, token) =>
            Dispatcher.Invoke(() => settingsVm.CurrentMcpToken = token);

        // 「重新生成」按钮：IssueMcpTokenAsync 内部会先撤销旧 token 再签发新 token。
        settingsVm.RegenerateMcpTokenRequested += async (_, _) =>
        {
            try
            {
                if (_aiSession is not null)
                    await _aiSession.IssueMcpTokenAsync(
                        TimeSpan.FromMinutes(settingsVm.McpTokenLifetimeMinutes),
                        settingsVm.McpAllowDestructiveTools);
            }
            catch (Exception ex) { SetStatus($"重新生成 Token 失败: {ex.Message}"); }
        };

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

    private async void OnSettingsChanged(object? sender, AIWhiteboardSettings settings)
    {
        SaveSettings(settings);
        var session = _manager.GetExportedSession();
        if (session is null) return;
        await DisposeAiSessionAsync();
        await InitAiSessionAsync(session);
        SetStatus($"设置已保存并应用: {settings.LlmProviderType}/{settings.LlmModel}");
    }

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

    private async Task DisposeAiSessionAsync()
    {
        if (_aiChatVm is not null)
        {
            _aiChatVm.SettingsVm.TestConnectionRequested -= OnTestConnectionRequested;
            _aiChatVm.SettingsChanged -= OnSettingsChanged;
            _aiChatVm.Dispose();
            _aiChatVm = null;
        }

        // 先停 HTTP server（停止接受新连接），再 dispose session（其内部会撤销 token，
        // 需要 registry 仍存活），最后才 dispose registry。
        if (_mcpHost is not null)
        {
            await _mcpHost.DisposeAsync();
            _mcpHost = null;
        }

        if (_aiSession is not null)
        {
            await _aiSession.DisposeAsync();
            _aiSession = null;
        }

        _mcpRegistry?.Dispose();
        _mcpRegistry = null;

        AiPanel.DataContext = null;
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