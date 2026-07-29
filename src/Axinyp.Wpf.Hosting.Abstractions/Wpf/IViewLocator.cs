namespace Axinyp.Wpf.Hosting.Abstractions.Wpf;

/// <summary>
/// 提供显式注册的 ViewModel 类型到 View 类型的一对一映射。
/// 不使用命名约定、程序集扫描或基类回退，以保持模块边界和诊断确定性。
/// </summary>
public interface IViewLocator
{
    /// <summary>返回 ViewModel 精确类型已注册的 View 类型。</summary>
    /// <exception cref="InvalidOperationException">未注册映射时抛出。</exception>
    Type GetViewType(Type viewModelType);
}
