namespace WhiteBoard.Contracts.Services;

public interface ISaveService
{
    Task SaveAsync(string documentId, Stream data, CancellationToken ct = default);

    Task<Stream?> LoadAsync(string documentId, CancellationToken ct = default);

    Task<bool> ExistsAsync(string documentId, CancellationToken ct = default);
}