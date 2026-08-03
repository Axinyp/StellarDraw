namespace Axinyp.Wpf.Hosting.Abstractions.Plugins;

/// <summary>插件发现失败的稳定、安全诊断上下文。</summary>
public sealed record PluginLoadFailureMetadata(
    string DiagnosticCode,
    int EventId,
    string? EntryAssembly = null,
    string? DependencyId = null,
    string? ExceptionType = null);
