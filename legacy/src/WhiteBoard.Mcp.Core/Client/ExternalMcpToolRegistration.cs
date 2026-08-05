using Microsoft.Extensions.AI;

namespace WhiteBoard.Mcp.Core.Client;

public sealed record ExternalMcpToolPolicy(
    bool RequiresConfirmation,
    bool IsDestructive,
    bool AllowInAutoApproveMode);

public sealed class ExternalMcpToolRegistration
{
    public string ConnectionId { get; }
    public string OriginalName { get; }
    public string PrefixedName { get; }
    public AIFunction Function { get; }
    public ExternalMcpToolPolicy Policy { get; }

    public ExternalMcpToolRegistration(
        string connectionId,
        string originalName,
        string prefixedName,
        AIFunction function,
        ExternalMcpToolPolicy policy)
    {
        ConnectionId = connectionId;
        OriginalName = originalName;
        PrefixedName = prefixedName;
        Function = function;
        Policy = policy;
    }
}