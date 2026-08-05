namespace WhiteBoard.Mcp.Core.Client;

public interface IMcpClientProvider : IAsyncDisposable
{
    ValueTask InitializeAsync(
        IReadOnlyList<ExternalMcpConnectionSpec> connections,
        CancellationToken ct = default);

    ValueTask<IReadOnlyList<ExternalMcpToolRegistration>> GetToolRegistrationsAsync(
        CancellationToken ct = default);
}