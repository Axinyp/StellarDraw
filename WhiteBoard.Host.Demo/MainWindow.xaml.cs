using System.IO;
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
    }

    // ── AI 面板切换 ───────────────────────────────────────────────────────────

    private void OnAiToggleChecked(object sender, RoutedEventArgs e)
        => ColAi.Width = AiPanelWidth;

    private void OnAiToggleUnchecked(object sender, RoutedEventArgs e)
        => ColAi.Width = new GridLength(0);

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
        var settings    = BuildDemoSettings();
        var secrets     = new EnvSecretStore();
        var llmProvider = new SettingsBackedLlmProvider(settings, secrets);

        _aiSession = new WhiteboardAISession(llmProvider, session);
        _aiChatVm  = new AiChatViewModel(_aiSession);
        AiPanel.DataContext = _aiChatVm;

        TxtInfo.Text += $"  |  AI: {settings.LlmProviderType}/{settings.LlmModel}";
    }

    private void DisposeAiSession()
    {
        _aiChatVm?.Dispose();
        _aiChatVm = null;

        if (_aiSession is not null)
        {
            // 同步路径仅在确保不会并发调用时使用（UI 线程单入口）
            _ = _aiSession.DisposeAsync().AsTask();
            _aiSession = null;
        }

        AiPanel.DataContext = null;
    }

    private async Task DisposeAiSessionAsync()
    {
        _aiChatVm?.Dispose();
        _aiChatVm = null;

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

    // ── 设置读取（Demo 级别，从环境变量）────────────────────────────────────────

    private static AIWhiteboardSettings BuildDemoSettings() => new()
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
