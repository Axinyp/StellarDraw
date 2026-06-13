using System.Collections.ObjectModel;
using System.Windows.Input;
using WhiteBoard.AI.Session;
using WhiteBoard.AI.Settings;

namespace WhiteBoard.UI.ViewModels;

/// <summary>
/// AI 设置面板 ViewModel。
/// 绑定到设置对话框中的 AI 配置页，修改后触发 SaveRequested 让宿主持久化。
/// </summary>
public sealed class AiSettingsViewModel : ViewModelBase
{
    private string _llmProvider = "ollama";
    private string _llmEndpoint = "http://localhost:11434";
    private string _llmModel    = "qwen2.5";
    private string _llmApiKeyRef = "";
    private string _executionMode = "ConfirmRequired";
    private bool   _sttEnabled  = true;
    private string _sttProviderType = "whisper";
    private string _sttLanguage = "zh";
    private string _sttModelPath = "";
    private string _sttEndpoint = "";
    private string _voiceMode   = "push-to-talk";
    private bool   _wakeWordEnabled = false;
    private string _wakeWord    = "嘿白板";
    private string _wakeWordProviderType = "windows";
    private int    _wakeWordUtteranceSeconds = 5;
    private bool   _ttsEnabled  = false;
    private string _ttsProviderType = "windows";
    private bool   _showStatusBar = true;
    private string _connectionTestStatus = "";
    private bool   _isTestingConnection;

    // ── MCP Server ────────────────────────────────────────────────────────────

    private bool   _mcpServerEnabled;
    private bool   _mcpServerStdioEnabled = true;
    private bool   _mcpServerHttpEnabled  = true;
    private bool   _mcpServerSseEnabled   = false;
    private string _mcpServerHttpUrl      = "http://127.0.0.1:31888";
    private string _mcpServerRoutePrefix  = "/mcp";
    private int    _mcpTokenLifetimeMinutes = 60;
    private bool   _mcpAllowDestructiveTools = false;
    private string _currentMcpToken       = "";

    // ── Providers ──────────────────────────────────────────────────────────────

    public IReadOnlyList<string> LlmProviders { get; } =
        ["ollama", "openai", "anthropic", "azure", "custom"];

    public IReadOnlyList<string> ExecutionModes { get; } =
        ["ConfirmRequired", "AutoApprove"];

    public IReadOnlyList<string> VoiceModes { get; } =
        ["push-to-talk", "wake-word"];

    public IReadOnlyList<string> SttProviders { get; } =
        ["whisper", "windows", "azure", "custom"];

    public IReadOnlyList<string> TtsProviders { get; } =
        ["windows", "azure", "custom"];

    // ── Properties ─────────────────────────────────────────────────────────────

    public string LlmProvider
    {
        get => _llmProvider;
        set { if (Set(ref _llmProvider, value)) {
            OnPropertyChanged(nameof(IsEndpointVisible));
            OnPropertyChanged(nameof(IsApiKeyVisible));
        }}
    }

    public string LlmEndpoint
    {
        get => _llmEndpoint;
        set => Set(ref _llmEndpoint, value);
    }

    public string LlmModel
    {
        get => _llmModel;
        set => Set(ref _llmModel, value);
    }

    public string LlmApiKeyRef
    {
        get => _llmApiKeyRef;
        set => Set(ref _llmApiKeyRef, value);
    }

    public string ExecutionMode
    {
        get => _executionMode;
        set => Set(ref _executionMode, value);
    }

    public bool SttEnabled
    {
        get => _sttEnabled;
        set => Set(ref _sttEnabled, value);
    }

    public string SttProviderType
    {
        get => _sttProviderType;
        set { if (Set(ref _sttProviderType, value)) OnPropertyChanged(nameof(IsWhisperStt)); }
    }

    public string SttLanguage
    {
        get => _sttLanguage;
        set => Set(ref _sttLanguage, value);
    }

    public string SttModelPath
    {
        get => _sttModelPath;
        set => Set(ref _sttModelPath, value);
    }

    public string SttEndpoint
    {
        get => _sttEndpoint;
        set => Set(ref _sttEndpoint, value);
    }

    public bool IsWhisperStt => _sttProviderType == "whisper";

    public string VoiceMode
    {
        get => _voiceMode;
        set { if (Set(ref _voiceMode, value)) OnPropertyChanged(nameof(IsWakeWordMode)); }
    }

    public bool IsWakeWordMode => _voiceMode == "wake-word";

    public bool WakeWordEnabled
    {
        get => _wakeWordEnabled;
        set => Set(ref _wakeWordEnabled, value);
    }

    public string WakeWord
    {
        get => _wakeWord;
        set => Set(ref _wakeWord, value);
    }

    public string WakeWordProviderType
    {
        get => _wakeWordProviderType;
        set => Set(ref _wakeWordProviderType, value);
    }

    public int WakeWordUtteranceSeconds
    {
        get => _wakeWordUtteranceSeconds;
        set => Set(ref _wakeWordUtteranceSeconds, value);
    }

    public bool TtsEnabled
    {
        get => _ttsEnabled;
        set => Set(ref _ttsEnabled, value);
    }

    public string TtsProviderType
    {
        get => _ttsProviderType;
        set => Set(ref _ttsProviderType, value);
    }

    public bool ShowStatusBar
    {
        get => _showStatusBar;
        set => Set(ref _showStatusBar, value);
    }

    public string ConnectionTestStatus
    {
        get => _connectionTestStatus;
        set
        {
            if (Set(ref _connectionTestStatus, value))
                OnPropertyChanged(nameof(ConnectionTestStatusBrush));
        }
    }

    public string ConnectionTestStatusBrush =>
        _connectionTestStatus.StartsWith("✓") ? "#2E7D32" :
        _connectionTestStatus.StartsWith("✗") ? "#C62828" : "#757575";

    public bool IsTestingConnection
    {
        get => _isTestingConnection;
        set
        {
            if (Set(ref _isTestingConnection, value))
                (TestConnectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool IsEndpointVisible =>
        LlmProvider is "ollama" or "azure" or "custom";

    public bool IsApiKeyVisible =>
        LlmProvider is not "ollama";

    // ── MCP Server Properties ─────────────────────────────────────────────────

    public bool McpServerEnabled
    {
        get => _mcpServerEnabled;
        set => Set(ref _mcpServerEnabled, value);
    }

    public bool McpServerStdioEnabled
    {
        get => _mcpServerStdioEnabled;
        set => Set(ref _mcpServerStdioEnabled, value);
    }

    public bool McpServerHttpEnabled
    {
        get => _mcpServerHttpEnabled;
        set => Set(ref _mcpServerHttpEnabled, value);
    }

    public bool McpServerSseEnabled
    {
        get => _mcpServerSseEnabled;
        set => Set(ref _mcpServerSseEnabled, value);
    }

    public string McpServerHttpUrl
    {
        get => _mcpServerHttpUrl;
        set => Set(ref _mcpServerHttpUrl, value);
    }

    public string McpServerRoutePrefix
    {
        get => _mcpServerRoutePrefix;
        set => Set(ref _mcpServerRoutePrefix, value);
    }

    public int McpTokenLifetimeMinutes
    {
        get => _mcpTokenLifetimeMinutes;
        set => Set(ref _mcpTokenLifetimeMinutes, value);
    }

    public bool McpAllowDestructiveTools
    {
        get => _mcpAllowDestructiveTools;
        set => Set(ref _mcpAllowDestructiveTools, value);
    }

    public string CurrentMcpToken
    {
        get => _currentMcpToken;
        set
        {
            if (Set(ref _currentMcpToken, value))
                OnPropertyChanged(nameof(ClaudeDesktopConfigSnippet));
        }
    }

    public string ClaudeDesktopConfigSnippet =>
        "{\n  \"mcpServers\": {\n    \"whiteboard\": {\n      \"command\": \"WhiteBoard.McpHost.exe\",\n"
        + "      \"env\": {\n        \"WHITEBOARD_MCP_TOKEN\": \"" + CurrentMcpToken + "\",\n"
        + "        \"WHITEBOARD_MCP_PIPE_NAME\": \"whiteboard-mcp\"\n      }\n    }\n  }\n}";

    // ── MCP Client connections ────────────────────────────────────────────────

    public ObservableCollection<McpClientConnectionItemViewModel> McpConnections { get; } = [];

    public ICommand SaveCommand               { get; }
    public ICommand ResetCommand              { get; }
    public ICommand RegenerateMcpTokenCommand { get; }
    public ICommand CopyMcpTokenCommand       { get; }
    public ICommand CopyClaudeConfigCommand   { get; }
    public ICommand AddMcpConnectionCommand   { get; }
    public ICommand RemoveMcpConnectionCommand { get; }
    public ICommand TestConnectionCommand     { get; }

    // ── Events ────────────────────────────────────────────────────────────────

    public event EventHandler<AIWhiteboardSettings>? SaveRequested;
    public event EventHandler? RegenerateMcpTokenRequested;
    public event EventHandler<AIWhiteboardSettings>? TestConnectionRequested;

    // ── Constructor ───────────────────────────────────────────────────────────

    public AiSettingsViewModel()
    {
        SaveCommand                = new RelayCommand(ExecuteSave);
        ResetCommand               = new RelayCommand(ExecuteReset);
        RegenerateMcpTokenCommand  = new RelayCommand(() => RegenerateMcpTokenRequested?.Invoke(this, EventArgs.Empty));
        CopyMcpTokenCommand        = new RelayCommand(() => CopyToClipboard(CurrentMcpToken));
        CopyClaudeConfigCommand    = new RelayCommand(() => CopyToClipboard(ClaudeDesktopConfigSnippet));
        AddMcpConnectionCommand    = new RelayCommand(() => McpConnections.Add(new McpClientConnectionItemViewModel()));
        RemoveMcpConnectionCommand = new RelayCommand<McpClientConnectionItemViewModel>(
            item => { if (item is not null) McpConnections.Remove(item); });
        TestConnectionCommand      = new RelayCommand(
            () => TestConnectionRequested?.Invoke(this, ToSettings()),
            () => !IsTestingConnection);
    }

    // ── Methods ───────────────────────────────────────────────────────────────

    public void LoadFrom(AIWhiteboardSettings s)
    {
        LlmProvider    = s.LlmProviderType;
        LlmEndpoint    = s.LlmEndpoint;
        LlmModel       = s.LlmModel;
        LlmApiKeyRef   = s.LlmApiKeyRef;
        ExecutionMode  = s.ExecutionMode;
        SttEnabled     = s.SttEnabled;
        SttProviderType = s.SttProviderType;
        SttLanguage    = s.SttLanguage;
        SttModelPath   = s.SttModelPath;
        SttEndpoint    = s.SttEndpoint;
        VoiceMode      = s.VoiceMode;
        WakeWordEnabled           = s.WakeWordEnabled;
        WakeWord                  = s.WakeWord;
        WakeWordProviderType      = s.WakeWordProviderType;
        WakeWordUtteranceSeconds  = s.WakeWordUtteranceSeconds;
        TtsEnabled     = s.TtsEnabled;
        TtsProviderType = s.TtsProviderType;
        ShowStatusBar  = s.ShowToolCallStatusBar;

        McpServerEnabled         = s.McpServer.Enabled;
        McpServerStdioEnabled    = s.McpServer.StdioEnabled;
        McpServerHttpEnabled     = s.McpServer.HttpEnabled;
        McpServerSseEnabled      = s.McpServer.EnableLegacySse;
        McpServerHttpUrl         = s.McpServer.HttpUrl;
        McpServerRoutePrefix     = s.McpServer.RoutePrefix;
        McpTokenLifetimeMinutes  = s.McpServer.TokenLifetimeMinutes;
        McpAllowDestructiveTools = s.McpServer.AllowDestructiveTools;

        McpConnections.Clear();
        foreach (var conn in s.McpClient.Connections)
            McpConnections.Add(McpClientConnectionItemViewModel.From(conn));
    }

    public AIWhiteboardSettings ToSettings() => new()
    {
        LlmProviderType    = LlmProvider,
        LlmEndpoint        = LlmEndpoint,
        LlmModel           = LlmModel,
        LlmApiKeyRef       = LlmApiKeyRef,
        ExecutionMode      = ExecutionMode,
        SttEnabled         = SttEnabled,
        SttProviderType    = SttProviderType,
        SttLanguage        = SttLanguage,
        SttModelPath       = SttModelPath,
        SttEndpoint        = SttEndpoint,
        VoiceMode          = VoiceMode,
        WakeWordEnabled           = WakeWordEnabled,
        WakeWord                  = WakeWord,
        WakeWordProviderType      = WakeWordProviderType,
        WakeWordUtteranceSeconds  = WakeWordUtteranceSeconds,
        TtsEnabled         = TtsEnabled,
        TtsProviderType    = TtsProviderType,
        ShowToolCallStatusBar = ShowStatusBar,
        McpServer = new McpServerSettings
        {
            Enabled               = McpServerEnabled,
            StdioEnabled          = McpServerStdioEnabled,
            HttpEnabled           = McpServerHttpEnabled,
            EnableLegacySse       = McpServerSseEnabled,
            HttpUrl               = McpServerHttpUrl,
            RoutePrefix           = McpServerRoutePrefix,
            TokenLifetimeMinutes  = McpTokenLifetimeMinutes,
            AllowDestructiveTools = McpAllowDestructiveTools,
        },
        McpClient = new McpClientSettings
        {
            Enabled     = McpConnections.Count > 0,
            Connections = McpConnections.Select(c => c.ToSettings()).ToList(),
        },
    };

    private void ExecuteSave()  => SaveRequested?.Invoke(this, ToSettings());
    private void ExecuteReset() => LoadFrom(AIWhiteboardSettings.Default);

    private static void CopyToClipboard(string text)
    {
        try { System.Windows.Clipboard.SetText(text); }
        catch { /* UI-only best-effort */ }
    }
}

public sealed class McpClientConnectionItemViewModel : ViewModelBase
{
    private string _displayName    = "New Connection";
    private string _transport      = "stdio";
    private string _command        = "";
    private string _arguments      = "";
    private string _endpoint       = "";
    private string _toolNamePrefix = "ext";
    private string _trustLevel     = "restricted";
    private bool   _allowDestructiveTools = false;
    private string _connectionStatus = "Disconnected";
    private int    _toolCount       = 0;

    public string DisplayName
    {
        get => _displayName;
        set => Set(ref _displayName, value);
    }

    public string Transport
    {
        get => _transport;
        set { if (Set(ref _transport, value)) OnPropertyChanged(nameof(IsStdio)); }
    }

    public bool IsStdio => Transport == "stdio";

    public string Command
    {
        get => _command;
        set => Set(ref _command, value);
    }

    public string Arguments
    {
        get => _arguments;
        set => Set(ref _arguments, value);
    }

    public string Endpoint
    {
        get => _endpoint;
        set => Set(ref _endpoint, value);
    }

    public string ToolNamePrefix
    {
        get => _toolNamePrefix;
        set => Set(ref _toolNamePrefix, value);
    }

    public string TrustLevel
    {
        get => _trustLevel;
        set => Set(ref _trustLevel, value);
    }

    public bool AllowDestructiveTools
    {
        get => _allowDestructiveTools;
        set => Set(ref _allowDestructiveTools, value);
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set => Set(ref _connectionStatus, value);
    }

    public int ToolCount
    {
        get => _toolCount;
        set => Set(ref _toolCount, value);
    }

    public static McpClientConnectionItemViewModel From(McpClientConnectionSettings s) => new()
    {
        DisplayName          = s.DisplayName,
        Transport            = s.Transport,
        Command              = s.Command,
        Arguments            = string.Join(" ", s.Arguments),
        Endpoint             = s.Endpoint,
        ToolNamePrefix       = s.ToolNamePrefix,
        TrustLevel           = s.TrustLevel,
        AllowDestructiveTools = s.AllowDestructiveTools,
    };

    public McpClientConnectionSettings ToSettings() => new()
    {
        DisplayName          = DisplayName,
        Transport            = Transport,
        Command              = Command,
        Arguments            = Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(),
        Endpoint             = Endpoint,
        ToolNamePrefix       = ToolNamePrefix,
        TrustLevel           = TrustLevel,
        AllowDestructiveTools = AllowDestructiveTools,
    };
}
