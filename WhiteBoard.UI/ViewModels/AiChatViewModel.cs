using System.Collections.ObjectModel;
using System.Windows.Input;
using WhiteBoard.AI.Session;

namespace WhiteBoard.UI.ViewModels;

/// <summary>
/// AI 聊天边栏 ViewModel。
/// 管理对话消息列表、输入状态、工具调用状态条，以及 ConfirmRequired 确认弹窗的桥接。
/// </summary>
public sealed class AiChatViewModel : ViewModelBase, IDisposable
{
    private readonly WhiteboardAISession _aiSession;
    private CancellationTokenSource?     _currentCts;

    private string  _inputText      = "";
    private bool    _isProcessing;
    private bool    _isRecording;
    private string  _statusBarText  = "";
    private bool    _showStatusBar;
    private bool    _confirmVisible;
    private string  _confirmMessage = "";
    private Action? _confirmOk;
    private Action? _confirmCancel;
    private int     _mcpActiveConnections;
    private bool    _hasMcpActivity;

    // ── Message model ──────────────────────────────────────────────────────────

    public sealed class ChatMessage
    {
        public string  Author   { get; init; } = "";
        public string  Text     { get; set;  } = "";
        public bool    IsUser   { get; init; }
        public bool    IsError  { get; init; }
        public DateTime Time   { get; init; } = DateTime.Now;
    }

    // ── Observable collections ─────────────────────────────────────────────────

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    // ── Bindable properties ────────────────────────────────────────────────────

    public string InputText
    {
        get => _inputText;
        set => Set(ref _inputText, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set
        {
            if (Set(ref _isProcessing, value))
                OnPropertyChanged(nameof(CanSend));
        }
    }

    public bool CanSend => !IsProcessing && !string.IsNullOrWhiteSpace(InputText);

    public bool IsRecording
    {
        get => _isRecording;
        set => Set(ref _isRecording, value);
    }

    public string StatusBarText
    {
        get => _statusBarText;
        set => Set(ref _statusBarText, value);
    }

    public bool ShowStatusBar
    {
        get => _showStatusBar;
        set => Set(ref _showStatusBar, value);
    }

    public bool ConfirmVisible
    {
        get => _confirmVisible;
        set => Set(ref _confirmVisible, value);
    }

    public string ConfirmMessage
    {
        get => _confirmMessage;
        set => Set(ref _confirmMessage, value);
    }

    public int McpActiveConnections
    {
        get => _mcpActiveConnections;
        set => Set(ref _mcpActiveConnections, value);
    }

    public bool HasMcpActivity
    {
        get => _hasMcpActivity;
        set => Set(ref _hasMcpActivity, value);
    }

    // ── Commands ───────────────────────────────────────────────────────────────

    public ICommand SendCommand      { get; }
    public ICommand InterruptCommand { get; }
    public ICommand ClearCommand     { get; }
    public ICommand ConfirmOkCommand { get; }
    public ICommand ConfirmCancelCommand { get; }
    public ICommand PttStartCommand  { get; }
    public ICommand PttStopCommand   { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public AiChatViewModel(WhiteboardAISession aiSession, bool showStatusBar = true)
    {
        _aiSession    = aiSession;
        _showStatusBar = showStatusBar;

        SendCommand          = new RelayCommand(() => _ = SendAsync(),
                                   () => CanSend);
        InterruptCommand     = new RelayCommand(Interrupt, () => IsProcessing);
        ClearCommand         = new RelayCommand(Clear,     () => !IsProcessing);
        ConfirmOkCommand     = new RelayCommand(OnConfirmOk);
        ConfirmCancelCommand = new RelayCommand(OnConfirmCancel);
        PttStartCommand      = new RelayCommand(StartPtt,
                                   () => _aiSession.IsRecording == false);
        PttStopCommand       = new RelayCommand(() => _ = StopPttAsync(),
                                   () => _aiSession.IsRecording);

        _aiSession.VoiceTextReady += OnVoiceTextReady;
    }

    // ── Public methods ────────────────────────────────────────────────────────

    public async Task SendAsync()
    {
        var text = InputText.Trim();
        if (string.IsNullOrEmpty(text) || IsProcessing) return;

        InputText   = "";
        IsProcessing = true;

        Messages.Add(new ChatMessage { Author = "你", Text = text, IsUser = true });
        var assistantMsg = new ChatMessage { Author = "AI", Text = "" };
        Messages.Add(assistantMsg);

        _currentCts = new CancellationTokenSource();

        try
        {
            await foreach (var evt in _aiSession.SendMessageAsync(text, _currentCts.Token))
            {
                switch (evt)
                {
                    case TextChunkEvent chunk:
                        assistantMsg.Text += chunk.Chunk;
                        OnPropertyChanged(nameof(Messages)); // refresh binding
                        break;

                    case ToolCallingEvent toolCall:
                        StatusBarText = $"正在调用: {toolCall.ToolName}…";
                        break;

                    case ToolResultEvent toolResult:
                        StatusBarText = toolResult.Success
                            ? $"✓ {toolResult.ToolName}"
                            : $"✗ {toolResult.ToolName}: 失败";
                        break;

                    case ConfirmRequiredEvent confirm:
                        ShowConfirmDialog(confirm);
                        break;

                    case AgentErrorEvent error:
                        assistantMsg.Text = string.IsNullOrEmpty(assistantMsg.Text)
                            ? $"[错误] {error.Message}"
                            : assistantMsg.Text + $"\n\n[错误] {error.Message}";
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (string.IsNullOrEmpty(assistantMsg.Text))
                assistantMsg.Text = "[已中断]";
        }
        finally
        {
            IsProcessing  = false;
            StatusBarText = "";
            ConfirmVisible = false;
            _currentCts?.Dispose();
            _currentCts = null;
        }
    }

    public void Interrupt()
    {
        _aiSession.Interrupt();
        _currentCts?.Cancel();
    }

    public void Clear()
    {
        Messages.Clear();
        _aiSession.ClearHistory();
        StatusBarText = "";
    }

    private void StartPtt()
    {
        _aiSession.StartPtt();
        IsRecording   = true;
        StatusBarText = "正在录音…";
    }

    private async Task StopPttAsync()
    {
        IsRecording   = false;
        StatusBarText = "正在识别…";
        await _aiSession.StopPttAsync().ConfigureAwait(false);
        StatusBarText = "";
    }

    private void OnVoiceTextReady(object? sender, string text)
    {
        // Show the voice-recognized text as a user message bubble
        Messages.Add(new ChatMessage { Author = "你（语音）", Text = text, IsUser = true });
    }

    // ── Confirm dialog ────────────────────────────────────────────────────────

    private void ShowConfirmDialog(ConfirmRequiredEvent e)
    {
        _confirmOk     = e.Confirm;
        _confirmCancel = e.Cancel;
        ConfirmMessage = e.Description;
        ConfirmVisible = true;
    }

    private void OnConfirmOk()
    {
        _confirmOk?.Invoke();
        ConfirmVisible = false;
    }

    private void OnConfirmCancel()
    {
        _confirmCancel?.Invoke();
        ConfirmVisible = false;
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _aiSession.VoiceTextReady -= OnVoiceTextReady;
        _currentCts?.Cancel();
        _currentCts?.Dispose();
    }
}
