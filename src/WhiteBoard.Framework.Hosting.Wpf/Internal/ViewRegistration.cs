namespace WhiteBoard.Framework.Hosting.Wpf.Internal;

/// <summary>单个 ViewModel 到 View 的不可变显式注册。</summary>
internal sealed record ViewRegistration(Type ViewModelType, Type ViewType);
