namespace WhiteBoard.Framework.Hosting.Wpf;

/// <summary>导航确认上下文。</summary>
public sealed record NavigationContext(string RegionName, object? CurrentViewModel, Type? TargetViewModelType,
    object? Parameter, NavigationKind Kind);

public enum NavigationKind { Forward, Back }
