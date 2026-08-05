namespace Axinyp.Wpf.Hosting.Wpf.Internal;

/// <summary>后退栈项；不缓存 View、ViewModel 或 scope，返回时重新创建以避免视觉树和资源泄漏。</summary>
internal sealed record NavigationJournalEntry(Type ViewModelType, object? Parameter);
