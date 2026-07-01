using System.Windows.Input;
using System.Windows.Media;

namespace WhiteBoard.UI.ViewModels;

/// <summary>工具类型枚举：对应工具栏各按钮功能。</summary>
public enum ToolType
{ Pen, Highlighter, Marker, Eraser, Select, Hand }

/// <summary>
/// 工具栏 ViewModel：维护当前工具、画笔颜色、笔宽、抗锯齿状态。
/// 通过 <see cref="INotifyPropertyChanged"/> 驱动 <see cref="Controls.InkToolbar"/> 同步 UI 状态。
/// </summary>
public sealed class ToolbarViewModel : ViewModelBase
{
    private ToolType _currentTool = ToolType.Pen;
    private uint _penColor = 0xFF000000u;
    private float _thickness = 3f;
    private bool _isAntiAlias = true;

    public ToolType CurrentTool
    {
        get => _currentTool;
        set => Set(ref _currentTool, value);
    }

    public uint PenColor
    {
        get => _penColor;
        set { if (Set(ref _penColor, value)) OnPropertyChanged(nameof(PenBrush)); }
    }

    public float Thickness
    {
        get => _thickness;
        set => Set(ref _thickness, value);
    }

    public bool IsAntiAlias
    {
        get => _isAntiAlias;
        set => Set(ref _isAntiAlias, value);
    }

    public Brush PenBrush => new SolidColorBrush(ArgbToColor(_penColor));

    public bool IsPenActive => _currentTool == ToolType.Pen;
    public bool IsHighlighterActive => _currentTool == ToolType.Highlighter;
    public bool IsEraserActive => _currentTool == ToolType.Eraser;
    public bool IsSelectActive => _currentTool == ToolType.Select;
    public bool IsHandActive => _currentTool == ToolType.Hand;

    // Tool commands
    public ICommand SelectPenCommand => new RelayCommand(() => SetTool(ToolType.Pen));

    public ICommand SelectHighlighterCommand => new RelayCommand(() => SetTool(ToolType.Highlighter));
    public ICommand SelectMarkerCommand => new RelayCommand(() => SetTool(ToolType.Marker));
    public ICommand SelectEraserCommand => new RelayCommand(() => SetTool(ToolType.Eraser));
    public ICommand SelectSelectCommand => new RelayCommand(() => SetTool(ToolType.Select));
    public ICommand SelectHandCommand => new RelayCommand(() => SetTool(ToolType.Hand));

    private void SetTool(ToolType t)
    {
        CurrentTool = t;
        OnPropertyChanged(nameof(IsPenActive));
        OnPropertyChanged(nameof(IsHighlighterActive));
        OnPropertyChanged(nameof(IsEraserActive));
        OnPropertyChanged(nameof(IsSelectActive));
        OnPropertyChanged(nameof(IsHandActive));
    }

    private static Color ArgbToColor(uint argb)
        => Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb);
}

internal sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public event EventHandler? CanExecuteChanged;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public event EventHandler? CanExecuteChanged;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;

    public void Execute(object? parameter) => _execute((T?)parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}