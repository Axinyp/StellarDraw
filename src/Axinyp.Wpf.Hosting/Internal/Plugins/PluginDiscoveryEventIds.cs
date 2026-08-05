using Microsoft.Extensions.Logging;

namespace Axinyp.Wpf.Hosting.Internal.Plugins;

internal static class PluginDiscoveryEventIds
{
    public static readonly EventId DiscoveryFailure = new(1040, nameof(DiscoveryFailure));
}
