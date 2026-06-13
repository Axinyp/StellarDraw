using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WhiteBoard.UI.ViewModels;

/// <summary>
/// ViewModel 基类：实现 <see cref="INotifyPropertyChanged"/>，
/// 提供 <see cref="Set{T}"/> 辅助方法（变更比较 + 属性通知一体）。
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
