using Axinyp.Wpf.Hosting.Abstractions.Plugins;

namespace Axinyp.Wpf.Hosting.Internal.Plugins;

internal sealed record StartupPluginDiagnostic(PluginLoadFailure Failure, Exception? Exception);
