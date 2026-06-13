using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using WhiteBoard.AI.Providers;
using WhiteBoard.AI.Session;
using WhiteBoard.AI.Settings;
using WhiteBoard.Core.Domain;
using WhiteBoard.UI.ViewModels;

namespace WhiteBoard.Host.Demo;

public partial class MainWindow : Window
{
    private readonly PluginManager        _manager = new();
    private readonly InMemoryMessageBus   _bus     = new();
    private readonly MockHostCapabilities _host    = MockHostCapabilities.Instance;

    private static readonly GridLength AiPanelWidth = new(320, GridUnitType.Pixel);

    private WhiteboardAISession? _aiSession;
    private AiChatViewModel?     _aiChatVm;

    public MainWindow()
    {
        InitializeComponent();
        Closed  += async (_, _) => await TearDownAsync();
        Loaded  += async (_, _) => await AutoLoadAsync();
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
        session.CanvasWidth  = (float)e.NewSize.Width;
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
                InitAiSession(session);
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

    private void InitAiSession(WhiteboardSession session)
    {
        // 同步路径：调用时已确保旧 session 已被 DisposeAiSessionAsync 清理
        var settings    = LoadSettings();
        var secrets     = new EnvSecretStore();
        var llmProvider = new SettingsBackedLlmProvider(settings, secrets);

        // 以 PluginHost 当前实际尺寸初始化画布大小，后续通过 SizeChanged 持续同步
        if (PluginHost.ActualWidth > 0 && PluginHost.ActualHeight > 0)
        {
            session.CanvasWidth  = (float)PluginHost.ActualWidth;
            session.CanvasHeight = (float)PluginHost.ActualHeight;
        }

        var settingsVm = new AiSettingsViewModel();
        settingsVm.LoadFrom(settings);
        settingsVm.TestConnectionRequested += OnTestConnectionRequested;

        _aiSession = new WhiteboardAISession(llmProvider, session);
        _aiChatVm  = new AiChatViewModel(_aiSession, settingsVm);
        _aiChatVm.SettingsChanged += OnSettingsChanged;
        AiPanel.DataContext = _aiChatVm;

        TxtInfo.Text += $"  |  AI: {settings.LlmProviderType}/{settings.LlmModel}";
    }

    private async void OnSettingsChanged(object? sender, AIWhiteboardSettings settings)
    {
        SaveSettings(settings);
        var session = _manager.GetExportedSession();
        if (session is null) return;
        await DisposeAiSessionAsync();
        InitAiSession(session);
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
            var response = await client.CompleteAsync(
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

        if (_aiSession is not null)
        {
            await _aiSession.DisposeAsync();
            _aiSession = null;
        }

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
        LlmEndpoint     = Environment.GetEnvironmentVariable("WHITEBOARD_LLM_ENDPOINT") ?? "http://localhost:11434",
        LlmModel        = Environment.GetEnvironmentVariable("WHITEBOARD_LLM_MODEL")    ?? "qwen2.5",
        LlmApiKeyRef    = Environment.GetEnvironmentVariable("WHITEBOARD_LLM_APIKEY_REF") ?? "",
        ExecutionMode   = "ConfirmRequired",
    };

    private void SetStatus(string text) => TxtStatus.Text = text;
}

/// <summary>
/// Demo 用 ISecretStore：从环境变量读取密钥，无需 Windows Credential Manager。
/// WHITEBOARD_LLM_APIKEY 对应 LlmApiKeyRef；键名中的 "." 替换为 "_" 后大写查找。
/// </summary>
file sealed class EnvSecretStore : ISecretStore
{
    public string Load(string key)
        => Environment.GetEnvironmentVariable(key)
        ?? Environment.GetEnvironmentVariable(key.Replace(".", "_").ToUpperInvariant())
        ?? "";

    public void Save(string key, string value) { }
    public void Delete(string key) { }
    public bool Exists(string key) => !string.IsNullOrEmpty(Load(key));
}
